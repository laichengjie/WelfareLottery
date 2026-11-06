using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace WelfareLottery
{
    /// <summary>
    /// 界面列显示设置：保存到本地 SQLite（表 ColumnSetting），无记录时默认全部显示
    /// </summary>
    public static class ColumnSettingService
    {
        private const string TableName = "ColumnSetting";

        private static string ConnectionString => $"Data Source={LotteryService.DbPath}";

        private static void EnsureTable()
        {
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
CREATE TABLE IF NOT EXISTS {TableName} (
    FormName   TEXT NOT NULL,
    ColumnName TEXT NOT NULL,
    Visible    INTEGER NOT NULL,
    PRIMARY KEY (FormName, ColumnName)
) WITHOUT ROWID;";
            cmd.ExecuteNonQuery();
        }

        /// <summary>读取某界面的列显示设置；无记录返回空字典（表示默认全部显示）</summary>
        public static Dictionary<string, bool> Load(string formName)
        {
            var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            EnsureTable();

            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT ColumnName, Visible FROM {TableName} WHERE FormName = $form";
            cmd.Parameters.AddWithValue("$form", formName);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                result[reader.GetString(0)] = reader.GetInt32(1) != 0;

            return result;
        }

        /// <summary>保存某界面的列显示设置（先清空旧记录再写入）</summary>
        public static void Save(string formName, IDictionary<string, bool> settings)
        {
            EnsureTable();

            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var tx = conn.BeginTransaction();

            using (var delete = conn.CreateCommand())
            {
                delete.Transaction = tx;
                delete.CommandText = $"DELETE FROM {TableName} WHERE FormName = $form";
                delete.Parameters.AddWithValue("$form", formName);
                delete.ExecuteNonQuery();
            }

            using (var insert = conn.CreateCommand())
            {
                insert.Transaction = tx;
                insert.CommandText = $"INSERT OR REPLACE INTO {TableName} (FormName, ColumnName, Visible) VALUES ($form, $column, $visible)";
                insert.Parameters.AddWithValue("$form", formName);
                insert.Parameters.AddWithValue("$column", string.Empty);
                insert.Parameters.AddWithValue("$visible", 1);

                foreach (var pair in settings)
                {
                    insert.Parameters["$column"].Value = pair.Key;
                    insert.Parameters["$visible"].Value = pair.Value ? 1 : 0;
                    insert.ExecuteNonQuery();
                }
            }

            tx.Commit();
        }
    }
}
