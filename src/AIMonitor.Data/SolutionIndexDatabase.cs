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

        using SqliteCommand pragma = connection.CreateCommand();
        pragma.CommandText = "pragma journal_mode=wal; pragma foreign_keys=on;";
        pragma.ExecuteNonQuery();

        return connection;
    }

    public void EnsureCreated()
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteTransaction transaction = connection.BeginTransaction();

        Execute(connection, transaction, """
            create table if not exists solution_state (
                id integer primary key check (id = 1),
                input_path text not null,
                indexed_at_utc text not null,
                project_count integer not null,
                document_count integer not null,
                diagnostic_count integer not null
            );
            """);

        Execute(connection, transaction, """
            create table if not exists projects (
                id integer primary key autoincrement,
                stable_key text not null unique,
                name text not null,
                project_path text not null unique,
                language text not null,
                target_framework text not null,
                target_frameworks text not null,
                output_type text not null,
                sdk text not null,
                assembly_name text not null,
                root_namespace text not null,
                nullable text not null,
                implicit_usings text not null,
                lang_version text not null,
                preprocessor_symbols text not null
            );
            """);

        Execute(connection, transaction, """
            create table if not exists documents (
                id integer primary key autoincrement,
                project_id integer not null references projects(id) on delete cascade,
                stable_key text not null unique,
                name text not null,
                file_path text not null,
                folders text not null,
                unique(project_id, file_path)
            );
            """);

        Execute(connection, transaction, """
            create table if not exists symbols (
                id integer primary key autoincrement,
                project_id integer not null references projects(id) on delete cascade,
                stable_key text not null unique,
                name text not null,
                kind text not null,
                namespace text not null,
                containing_type text not null,
                file_path text not null,
                start_line integer not null,
                end_line integer not null,
                signature text not null
            );
            """);

        Execute(connection, transaction, """
            create table if not exists symbol_references (
                id integer primary key autoincrement,
                project_id integer not null references projects(id) on delete cascade,
                target_stable_key text not null,
                file_path text not null,
                line integer not null,
                column integer not null,
                reference_kind text not null,
                snippet text not null
            );
            """);

        Execute(connection, transaction, """
            create table if not exists project_references (
                id integer primary key autoincrement,
                project_id integer not null references projects(id) on delete cascade,
                include text not null,
                full_path text not null
            );
            """);

        Execute(connection, transaction, """
            create table if not exists package_references (
                id integer primary key autoincrement,
                project_id integer not null references projects(id) on delete cascade,
                include text not null,
                version text not null
            );
            """);

        Execute(connection, transaction, """
            create table if not exists framework_references (
                id integer primary key autoincrement,
                project_id integer not null references projects(id) on delete cascade,
                include text not null
            );
            """);

        Execute(connection, transaction, """
            create table if not exists global_usings (
                id integer primary key autoincrement,
                project_id integer not null references projects(id) on delete cascade,
                include text not null,
                is_static text not null,
                alias text not null
            );
            """);

        Execute(connection, transaction, """
            create table if not exists diagnostics (
                id integer primary key autoincrement,
                message text not null
            );
            """);

        Execute(connection, transaction, "create index if not exists idx_projects_path on projects(project_path);");
        Execute(connection, transaction, "create index if not exists idx_projects_stable_key on projects(stable_key);");
        Execute(connection, transaction, "create index if not exists idx_documents_file on documents(file_path);");
        Execute(connection, transaction, "create index if not exists idx_documents_stable_key on documents(stable_key);");
        Execute(connection, transaction, "create index if not exists idx_symbols_name on symbols(name);");
        Execute(connection, transaction, "create index if not exists idx_symbols_file on symbols(file_path);");
        Execute(connection, transaction, "create index if not exists idx_symbol_references_target on symbol_references(target_stable_key);");
        Execute(connection, transaction, "create index if not exists idx_symbol_references_file on symbol_references(file_path);");
        Execute(connection, transaction, "create index if not exists idx_project_references_full_path on project_references(full_path);");
        Execute(connection, transaction, "create index if not exists idx_package_references_include on package_references(include);");
        Execute(connection, transaction, "create index if not exists idx_framework_references_include on framework_references(include);");
        Execute(connection, transaction, "create index if not exists idx_global_usings_include on global_usings(include);");

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
