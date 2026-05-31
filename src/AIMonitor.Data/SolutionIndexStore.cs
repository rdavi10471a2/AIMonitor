using AIMonitor.MSBuild;
using Microsoft.Data.Sqlite;

namespace AIMonitor.Data;

public sealed class SolutionIndexStore
{
    private readonly SolutionIndexDatabase database;

    public SolutionIndexStore(SolutionIndexDatabase database)
    {
        this.database = database;
    }

    public SolutionIndexRunSummary SaveSnapshot(MSBuildSolutionSnapshot snapshot)
    {
        database.EnsureCreated();

        using SqliteConnection connection = database.OpenConnection();
        using SqliteTransaction transaction = connection.BeginTransaction();
        long runId = InsertRun(connection, transaction, snapshot);

        Execute(connection, transaction, """
            insert into indexed_solutions(run_id, input_path)
            values ($runId, $inputPath);
            """,
            ("$runId", runId),
            ("$inputPath", snapshot.InputPath));

        foreach (MSBuildProjectSnapshot project in snapshot.Projects)
        {
            Execute(connection, transaction, """
                insert into indexed_projects(run_id, name, project_path, language, preprocessor_symbols)
                values ($runId, $name, $projectPath, $language, $preprocessorSymbols);
                """,
                ("$runId", runId),
                ("$name", project.Name),
                ("$projectPath", project.ProjectPath),
                ("$language", project.Language),
                ("$preprocessorSymbols", string.Join(";", project.PreprocessorSymbols)));

            foreach (MSBuildDocumentSnapshot document in project.Documents)
            {
                Execute(connection, transaction, """
                    insert into indexed_documents(run_id, project_path, name, file_path, folders)
                    values ($runId, $projectPath, $name, $filePath, $folders);
                    """,
                    ("$runId", runId),
                    ("$projectPath", project.ProjectPath),
                    ("$name", document.Name),
                    ("$filePath", document.FilePath),
                    ("$folders", string.Join("/", document.Folders)));
            }
        }

        foreach (string diagnostic in snapshot.Diagnostics)
        {
            Execute(connection, transaction, """
                insert into index_diagnostics(run_id, message)
                values ($runId, $message);
                """,
                ("$runId", runId),
                ("$message", diagnostic));
        }

        transaction.Commit();
        return GetRunSummary(runId);
    }

    public SolutionIndexRunSummary GetRunSummary(long runId)
    {
        database.EnsureCreated();
        using SqliteConnection connection = database.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            select id, input_path, indexed_at_utc, project_count, document_count, diagnostic_count
            from index_runs
            where id = $runId;
            """;
        command.Parameters.AddWithValue("$runId", runId);

        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            throw new InvalidOperationException($"Index run {runId} was not found.");
        }

        return new SolutionIndexRunSummary(
            reader.GetInt64(0),
            reader.GetString(1),
            DateTimeOffset.Parse(reader.GetString(2)),
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetInt32(5));
    }

    public IReadOnlyList<IndexedDocumentRow> ListDocuments(long runId)
    {
        database.EnsureCreated();
        using SqliteConnection connection = database.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            select project_path, name, file_path, folders
            from indexed_documents
            where run_id = $runId
            order by file_path;
            """;
        command.Parameters.AddWithValue("$runId", runId);

        List<IndexedDocumentRow> rows = [];
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new IndexedDocumentRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3)));
        }

        return rows;
    }

    private static long InsertRun(SqliteConnection connection, SqliteTransaction transaction, MSBuildSolutionSnapshot snapshot)
    {
        int projectCount = snapshot.Projects.Count;
        int documentCount = snapshot.Projects.Sum(project => project.Documents.Count);
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            insert into index_runs(input_path, indexed_at_utc, project_count, document_count, diagnostic_count)
            values ($inputPath, $indexedAtUtc, $projectCount, $documentCount, $diagnosticCount);

            select last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$inputPath", snapshot.InputPath);
        command.Parameters.AddWithValue("$indexedAtUtc", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$projectCount", projectCount);
        command.Parameters.AddWithValue("$documentCount", documentCount);
        command.Parameters.AddWithValue("$diagnosticCount", snapshot.Diagnostics.Count);

        object? result = command.ExecuteScalar();
        return Convert.ToInt64(result);
    }

    private static void Execute(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string commandText,
        params (string Name, object? Value)[] parameters)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        foreach ((string name, object? value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        command.ExecuteNonQuery();
    }
}

public sealed record SolutionIndexRunSummary(
    long RunId,
    string InputPath,
    DateTimeOffset IndexedAtUtc,
    int ProjectCount,
    int DocumentCount,
    int DiagnosticCount);

public sealed record IndexedDocumentRow(
    string ProjectPath,
    string Name,
    string FilePath,
    string Folders);
