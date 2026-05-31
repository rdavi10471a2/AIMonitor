using Microsoft.Data.Sqlite;

namespace AIMonitor.Data;

public sealed class SolutionIndexDatabase
{
    private readonly string databasePath;

    public SolutionIndexDatabase(string databasePath)
    {
        this.databasePath = Path.GetFullPath(databasePath);
    }

    public string DatabasePath => databasePath;

    public SqliteConnection OpenConnection()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath) ?? ".");
        SqliteConnection connection = new($"Data Source={databasePath}");
        connection.Open();
        return connection;
    }

    public void EnsureCreated()
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteTransaction transaction = connection.BeginTransaction();

        Execute(connection, transaction, """
            create table if not exists index_runs (
                id integer primary key autoincrement,
                input_path text not null,
                indexed_at_utc text not null,
                project_count integer not null,
                document_count integer not null,
                diagnostic_count integer not null
            );
            """);

        Execute(connection, transaction, """
            create table if not exists indexed_solutions (
                id integer primary key autoincrement,
                run_id integer not null references index_runs(id) on delete cascade,
                input_path text not null
            );
            """);

        Execute(connection, transaction, """
            create table if not exists indexed_projects (
                id integer primary key autoincrement,
                run_id integer not null references index_runs(id) on delete cascade,
                name text not null,
                project_path text not null,
                language text not null,
                preprocessor_symbols text not null
            );
            """);

        Execute(connection, transaction, """
            create table if not exists indexed_documents (
                id integer primary key autoincrement,
                run_id integer not null references index_runs(id) on delete cascade,
                project_path text not null,
                name text not null,
                file_path text not null,
                folders text not null
            );
            """);

        Execute(connection, transaction, """
            create table if not exists index_diagnostics (
                id integer primary key autoincrement,
                run_id integer not null references index_runs(id) on delete cascade,
                message text not null
            );
            """);

        Execute(connection, transaction, "create index if not exists idx_indexed_projects_run on indexed_projects(run_id);");
        Execute(connection, transaction, "create index if not exists idx_indexed_documents_run on indexed_documents(run_id);");
        Execute(connection, transaction, "create index if not exists idx_indexed_documents_file on indexed_documents(file_path);");

        transaction.Commit();
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string commandText)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        command.ExecuteNonQuery();
    }
}
