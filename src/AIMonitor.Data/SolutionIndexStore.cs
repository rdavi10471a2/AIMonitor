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

    public SolutionIndexSummary SaveSnapshot(MSBuildSolutionSnapshot snapshot)
    {
        database.EnsureCreated();

        using SqliteConnection connection = database.OpenConnection();
        using SqliteTransaction transaction = connection.BeginTransaction();

        ClearCurrentState(connection, transaction);
        SaveSolutionState(connection, transaction, snapshot);

        foreach (MSBuildProjectSnapshot project in snapshot.Projects)
        {
            long projectId = InsertProject(connection, transaction, project);
            InsertDocuments(connection, transaction, projectId, project.Documents);
            InsertProjectReferences(connection, transaction, projectId, project.ProjectReferences);
            InsertPackageReferences(connection, transaction, projectId, project.PackageReferences);
            InsertFrameworkReferences(connection, transaction, projectId, project.FrameworkReferences);
            InsertGlobalUsings(connection, transaction, projectId, project.GlobalUsings);
        }

        foreach (string diagnostic in snapshot.Diagnostics)
        {
            Execute(connection, transaction, """
                insert into diagnostics(message)
                values ($message);
                """,
                ("$message", diagnostic));
        }

        transaction.Commit();
        return GetSummary();
    }

    public SolutionIndexSummary GetSummary()
    {
        database.EnsureCreated();
        using SqliteConnection connection = database.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            select input_path, indexed_at_utc, project_count, document_count, diagnostic_count
            from solution_state
            where id = 1;
            """;

        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return new SolutionIndexSummary(string.Empty, DateTimeOffset.MinValue, 0, 0, 0);
        }

        return new SolutionIndexSummary(
            reader.GetString(0),
            DateTimeOffset.Parse(reader.GetString(1)),
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.GetInt32(4));
    }

    public IReadOnlyList<IndexedDocumentRow> ListDocuments()
    {
        database.EnsureCreated();
        using SqliteConnection connection = database.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            select projects.project_path, documents.name, documents.file_path, documents.folders
            from documents
            inner join projects on projects.id = documents.project_id
            order by documents.file_path;
            """;

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

    public IReadOnlyList<IndexedProjectRow> ListProjects()
    {
        database.EnsureCreated();
        using SqliteConnection connection = database.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            select name, project_path, language, target_framework, target_frameworks, output_type,
                   sdk, assembly_name, root_namespace, nullable, implicit_usings, lang_version,
                   preprocessor_symbols
            from projects
            order by project_path;
            """;

        List<IndexedProjectRow> rows = [];
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new IndexedProjectRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetString(9),
                reader.GetString(10),
                reader.GetString(11),
                reader.GetString(12)));
        }

        return rows;
    }

    public IReadOnlyList<IndexedPackageReferenceRow> ListPackageReferences()
    {
        database.EnsureCreated();
        using SqliteConnection connection = database.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            select projects.project_path, package_references.include, package_references.version
            from package_references
            inner join projects on projects.id = package_references.project_id
            order by projects.project_path, package_references.include;
            """;

        List<IndexedPackageReferenceRow> rows = [];
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new IndexedPackageReferenceRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2)));
        }

        return rows;
    }

    private static void ClearCurrentState(SqliteConnection connection, SqliteTransaction transaction)
    {
        Execute(connection, transaction, "delete from diagnostics;");
        Execute(connection, transaction, "delete from global_usings;");
        Execute(connection, transaction, "delete from framework_references;");
        Execute(connection, transaction, "delete from package_references;");
        Execute(connection, transaction, "delete from project_references;");
        Execute(connection, transaction, "delete from documents;");
        Execute(connection, transaction, "delete from projects;");
        Execute(connection, transaction, "delete from solution_state;");
    }

    private static void SaveSolutionState(
        SqliteConnection connection,
        SqliteTransaction transaction,
        MSBuildSolutionSnapshot snapshot)
    {
        Execute(connection, transaction, """
            insert into solution_state(id, input_path, indexed_at_utc, project_count, document_count, diagnostic_count)
            values (1, $inputPath, $indexedAtUtc, $projectCount, $documentCount, $diagnosticCount);
            """,
            ("$inputPath", snapshot.InputPath),
            ("$indexedAtUtc", DateTimeOffset.UtcNow.ToString("O")),
            ("$projectCount", snapshot.Projects.Count),
            ("$documentCount", snapshot.Projects.Sum(project => project.Documents.Count)),
            ("$diagnosticCount", snapshot.Diagnostics.Count));
    }

    private static long InsertProject(
        SqliteConnection connection,
        SqliteTransaction transaction,
        MSBuildProjectSnapshot project)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            insert into projects(name, project_path, language, target_framework, target_frameworks,
                                 output_type, sdk, assembly_name, root_namespace, nullable,
                                 implicit_usings, lang_version, preprocessor_symbols)
            values ($name, $projectPath, $language, $targetFramework, $targetFrameworks,
                    $outputType, $sdk, $assemblyName, $rootNamespace, $nullable,
                    $implicitUsings, $langVersion, $preprocessorSymbols);

            select last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$name", project.Name);
        command.Parameters.AddWithValue("$projectPath", project.ProjectPath);
        command.Parameters.AddWithValue("$language", project.Language);
        command.Parameters.AddWithValue("$targetFramework", project.TargetFramework);
        command.Parameters.AddWithValue("$targetFrameworks", project.TargetFrameworks);
        command.Parameters.AddWithValue("$outputType", project.OutputType);
        command.Parameters.AddWithValue("$sdk", project.Sdk);
        command.Parameters.AddWithValue("$assemblyName", project.AssemblyName);
        command.Parameters.AddWithValue("$rootNamespace", project.RootNamespace);
        command.Parameters.AddWithValue("$nullable", project.Nullable);
        command.Parameters.AddWithValue("$implicitUsings", project.ImplicitUsings);
        command.Parameters.AddWithValue("$langVersion", project.LangVersion);
        command.Parameters.AddWithValue("$preprocessorSymbols", string.Join(";", project.PreprocessorSymbols));

        object? result = command.ExecuteScalar();
        return Convert.ToInt64(result);
    }

    private static void InsertDocuments(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long projectId,
        IReadOnlyList<MSBuildDocumentSnapshot> documents)
    {
        foreach (MSBuildDocumentSnapshot document in documents)
        {
            Execute(connection, transaction, """
                insert into documents(project_id, name, file_path, folders)
                values ($projectId, $name, $filePath, $folders);
                """,
                ("$projectId", projectId),
                ("$name", document.Name),
                ("$filePath", document.FilePath),
                ("$folders", string.Join("/", document.Folders)));
        }
    }

    private static void InsertProjectReferences(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long projectId,
        IReadOnlyList<MSBuildProjectReferenceSnapshot> references)
    {
        foreach (MSBuildProjectReferenceSnapshot reference in references)
        {
            Execute(connection, transaction, """
                insert into project_references(project_id, include, full_path)
                values ($projectId, $include, $fullPath);
                """,
                ("$projectId", projectId),
                ("$include", reference.Include),
                ("$fullPath", reference.FullPath));
        }
    }

    private static void InsertPackageReferences(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long projectId,
        IReadOnlyList<MSBuildPackageReferenceSnapshot> references)
    {
        foreach (MSBuildPackageReferenceSnapshot reference in references)
        {
            Execute(connection, transaction, """
                insert into package_references(project_id, include, version)
                values ($projectId, $include, $version);
                """,
                ("$projectId", projectId),
                ("$include", reference.Include),
                ("$version", reference.Version));
        }
    }

    private static void InsertFrameworkReferences(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long projectId,
        IReadOnlyList<MSBuildFrameworkReferenceSnapshot> references)
    {
        foreach (MSBuildFrameworkReferenceSnapshot reference in references)
        {
            Execute(connection, transaction, """
                insert into framework_references(project_id, include)
                values ($projectId, $include);
                """,
                ("$projectId", projectId),
                ("$include", reference.Include));
        }
    }

    private static void InsertGlobalUsings(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long projectId,
        IReadOnlyList<MSBuildGlobalUsingSnapshot> usings)
    {
        foreach (MSBuildGlobalUsingSnapshot globalUsing in usings)
        {
            Execute(connection, transaction, """
                insert into global_usings(project_id, include, is_static, alias)
                values ($projectId, $include, $isStatic, $alias);
                """,
                ("$projectId", projectId),
                ("$include", globalUsing.Include),
                ("$isStatic", globalUsing.Static),
                ("$alias", globalUsing.Alias));
        }
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
