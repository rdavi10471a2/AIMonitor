using AIMonitor.Core;
using Microsoft.Data.Sqlite;

namespace AIMonitor.Planning
{
    public sealed class PlanningDatabase
    {
        private readonly MonitorSettings settings;
        private readonly string databasePath;

        public PlanningDatabase(MonitorSettings settings)
            : this(settings, PlanningPaths.GetDefaultBoardDatabasePath(settings))
        {
        }

        public PlanningDatabase(MonitorSettings settings, string databasePath)
        {
            this.settings = settings;
            this.databasePath = Path.GetFullPath(databasePath);
        }

        public string DatabasePath => databasePath;

        public SqliteConnection OpenConnection()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(databasePath) ?? ".");
            SqliteConnection connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
            connection.Open();

            using (SqliteCommand pragma = connection.CreateCommand())
            {
                pragma.CommandText = "pragma journal_mode=wal; pragma foreign_keys=on;";
                pragma.ExecuteNonQuery();
            }

            return connection;
        }

        public void EnsureCreated()
        {
            using (SqliteConnection connection = OpenConnection())
            {
                using (SqliteTransaction transaction = connection.BeginTransaction())
                {
                    Execute(connection, transaction, """
                        create table if not exists board_state (
                            id integer primary key check (id = 1),
                            watched_solution_path text not null,
                            watched_project_folder text not null,
                            created_at_utc text not null,
                            updated_at_utc text not null,
                            active_task_id text not null default ''
                        );
                        """);

                    Execute(connection, transaction, """
                        create table if not exists tasks (
                            task_id text primary key,
                            title text not null,
                            description text not null default '',
                            goal text not null default '',
                            human_context text not null default '',
                            constraints_text text not null default '',
                            acceptance_criteria text not null default '',
                            status text not null,
                            priority integer not null default 0,
                            sort_order integer not null default 0,
                            task_memory_md_path text not null default '',
                            created_at_utc text not null,
                            updated_at_utc text not null,
                            closed_at_utc text not null default ''
                        );
                        """);

                    Execute(connection, transaction, """
                        create table if not exists task_events (
                            id integer primary key autoincrement,
                            task_id text not null references tasks(task_id) on delete cascade,
                            event_type text not null,
                            summary text not null,
                            payload_json text not null default '',
                            created_at_utc text not null
                        );
                        """);

                    Execute(connection, transaction, """
                        create table if not exists task_iterations (
                            iteration_id text primary key,
                            task_id text not null references tasks(task_id) on delete cascade,
                            sequence integer not null,
                            goal text not null,
                            status text not null,
                            created_at_utc text not null,
                            completed_at_utc text not null default '',
                            unique(task_id, sequence)
                        );
                        """);

                    Execute(connection, transaction, """
                        create table if not exists task_staged_records (
                            id integer primary key autoincrement,
                            task_id text not null references tasks(task_id) on delete cascade,
                            staged_record_id text not null,
                            session_id text not null default '',
                            relative_path text not null,
                            staged_hash text not null,
                            status text not null,
                            created_at_utc text not null,
                            unique(staged_record_id)
                        );
                        """);

                    Execute(connection, transaction, """
                        create table if not exists task_decisions (
                            id integer primary key autoincrement,
                            task_id text not null references tasks(task_id) on delete cascade,
                            staged_record_id text not null,
                            decision text not null,
                            classification text not null,
                            relative_path text not null,
                            status text not null,
                            message text not null default '',
                            decided_at_utc text not null,
                            payload_json text not null default ''
                        );
                        """);

                    Execute(connection, transaction, "create index if not exists idx_tasks_status on tasks(status);");
                    Execute(connection, transaction, "create index if not exists idx_task_events_task on task_events(task_id);");
                    Execute(connection, transaction, "create index if not exists idx_task_iterations_task on task_iterations(task_id, sequence);");
                    Execute(connection, transaction, "create index if not exists idx_task_staged_records_task on task_staged_records(task_id);");
                    Execute(connection, transaction, "create index if not exists idx_task_decisions_task on task_decisions(task_id);");
                    Execute(connection, transaction, "create index if not exists idx_task_decisions_staged_record on task_decisions(staged_record_id);");

                    EnsureBoardState(connection, transaction);
                    transaction.Commit();
                }
            }
        }

        private void EnsureBoardState(SqliteConnection connection, SqliteTransaction transaction)
        {
            string now = DateTimeOffset.UtcNow.ToString("O");
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    insert or ignore into board_state (
                        id,
                        watched_solution_path,
                        watched_project_folder,
                        created_at_utc,
                        updated_at_utc,
                        active_task_id
                    )
                    values (
                        1,
                        $watched_solution_path,
                        $watched_project_folder,
                        $created_at_utc,
                        $updated_at_utc,
                        ''
                    );
                    """;
                command.Parameters.AddWithValue("$watched_solution_path", settings.WatchedSolutionPath);
                command.Parameters.AddWithValue("$watched_project_folder", settings.WatchedProjectFolder);
                command.Parameters.AddWithValue("$created_at_utc", now);
                command.Parameters.AddWithValue("$updated_at_utc", now);
                command.ExecuteNonQuery();
            }
        }

        internal static void Execute(SqliteConnection connection, SqliteTransaction transaction, string commandText)
        {
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = commandText;
                command.ExecuteNonQuery();
            }
        }
    }
}
