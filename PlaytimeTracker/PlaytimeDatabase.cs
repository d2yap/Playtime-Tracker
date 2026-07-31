using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;

namespace PlaytimeTracker;

public class PlaytimeDatabase
{
    private readonly string connectionString;

    public PlaytimeDatabase(string dbPath)
    {
        connectionString = $"Data Source={dbPath}";
        Initialize();
    }

    private void Initialize()
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        var command = connection.CreateCommand();
        command.CommandText =
        @"
            CREATE TABLE IF NOT EXISTS Playtime (
                Date TEXT PRIMARY KEY,
                Seconds REAL NOT NULL
            );
        ";
        command.ExecuteNonQuery();
    }

    public void SaveTodayPlaytime(DateTime date, TimeSpan playtime)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        var command = connection.CreateCommand();
        command.CommandText =
        @"
            INSERT INTO Playtime (Date, Seconds)
            VALUES ($date, $seconds)
            ON CONFLICT(Date) DO UPDATE SET Seconds = $seconds;
        ";
        command.Parameters.AddWithValue("$date", date.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$seconds", playtime.TotalSeconds);
        command.ExecuteNonQuery();
    }

    public TimeSpan GetPlaytimeForDate(DateTime date)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Seconds FROM Playtime WHERE Date = $date;";
        command.Parameters.AddWithValue("$date", date.ToString("yyyy-MM-dd"));

        var result = command.ExecuteScalar();
        return result != null ? TimeSpan.FromSeconds((double)result) : TimeSpan.Zero;
    }


    public Dictionary<DateTime, TimeSpan> GetAllPlaytime()
    {
        var results = new Dictionary<DateTime, TimeSpan>();

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Date, Seconds FROM Playtime ORDER BY Date;";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var date = DateTime.Parse(reader.GetString(0));
            var seconds = reader.GetDouble(1);
            results[date] = TimeSpan.FromSeconds(seconds);
        }

        return results;
    }
}
