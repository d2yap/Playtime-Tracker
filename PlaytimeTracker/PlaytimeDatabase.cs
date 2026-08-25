
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

        // Enable foreign keys
        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys = ON;";
            pragma.ExecuteNonQuery();
        }

        // New database structure
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
            @"
                CREATE TABLE IF NOT EXISTS Dates (
                    DateID INTEGER PRIMARY KEY AUTOINCREMENT,
                    Date TEXT NOT NULL UNIQUE
                );

                CREATE TABLE IF NOT EXISTS Jobs (
                    JobID INTEGER PRIMARY KEY AUTOINCREMENT,
                    JobName TEXT NOT NULL,
                    JobAbbreviation TEXT NOT NULL UNIQUE
                );

                CREATE TABLE IF NOT EXISTS DailyPlaytime (
                    PlaytimeID INTEGER PRIMARY KEY AUTOINCREMENT,
                    DateID INTEGER NOT NULL,
                    JobID INTEGER NOT NULL,
                    Seconds REAL NOT NULL,

                    UNIQUE(DateID, JobID),

                    FOREIGN KEY(DateID)
                        REFERENCES Dates(DateID)
                        ON DELETE CASCADE,

                    FOREIGN KEY(JobID)
                        REFERENCES Jobs(JobID)
                        ON DELETE CASCADE
                );
            ";

            command.ExecuteNonQuery();
        }

    }

    // Date

    private long GetOrCreateDateId(DateTime date)
    {
        string dateString = date.ToString("yyyy-MM-dd");

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        // Create the date if it doesn't exist.
        using (var insert = connection.CreateCommand())
        {
            insert.CommandText =
            @"
                INSERT OR IGNORE INTO Dates (Date)
                VALUES ($date);
            ";

            insert.Parameters.AddWithValue("$date", dateString);
            insert.ExecuteNonQuery();
        }

        // Get the DateID.
        using (var select = connection.CreateCommand())
        {
            select.CommandText =
            @"
                SELECT DateID
                FROM Dates
                WHERE Date = $date;
            ";

            select.Parameters.AddWithValue("$date", dateString);

            return Convert.ToInt64(select.ExecuteScalar());
        }
    }

    // create job if it doesn't exist

    public long GetOrCreateJob(string jobName, string jobAbbreviation)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        // Try to find the job first.
        using (var select = connection.CreateCommand())
        {
            select.CommandText =
            @"
                SELECT JobID
                FROM Jobs
                WHERE JobAbbreviation = $abbreviation;
            ";

            select.Parameters.AddWithValue(
                "$abbreviation",
                jobAbbreviation);

            var result = select.ExecuteScalar();

            if (result != null)
                return Convert.ToInt64(result);
        }

        // Job doesn't exist yet.
        using (var insert = connection.CreateCommand())
        {
            insert.CommandText =
            @"
                INSERT INTO Jobs
                    (JobName, JobAbbreviation)
                VALUES
                    ($name, $abbreviation);

                SELECT last_insert_rowid();
            ";

            insert.Parameters.AddWithValue("$name", jobName);
            insert.Parameters.AddWithValue(
                "$abbreviation",
                jobAbbreviation);

            return Convert.ToInt64(insert.ExecuteScalar());
        }
    }

    // Playtime saved

    public void SaveJobPlaytime(
        DateTime date,
        string jobName,
        string jobAbbreviation,
        TimeSpan playtime)
    {
        long dateId = GetOrCreateDateId(date);

        long jobId = GetOrCreateJob(
            jobName,
            jobAbbreviation);

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        // First, check if the record already exists
        using (var checkCommand = connection.CreateCommand())
        {
            checkCommand.CommandText =
            @"
                SELECT COUNT(*)
                FROM DailyPlaytime
                WHERE DateID = $dateId AND JobID = $jobId;
            ";

            checkCommand.Parameters.AddWithValue("$dateId", dateId);
            checkCommand.Parameters.AddWithValue("$jobId", jobId);

            var count = Convert.ToInt64(checkCommand.ExecuteScalar());

            if (count > 0)
            {
                // Record exists, use UPDATE to avoid incrementing the AUTOINCREMENT
                using var updateCommand = connection.CreateCommand();

                updateCommand.CommandText =
                @"
                    UPDATE DailyPlaytime
                    SET Seconds = $seconds
                    WHERE DateID = $dateId AND JobID = $jobId;
                ";

                updateCommand.Parameters.AddWithValue("$seconds", playtime.TotalSeconds);
                updateCommand.Parameters.AddWithValue("$dateId", dateId);
                updateCommand.Parameters.AddWithValue("$jobId", jobId);

                updateCommand.ExecuteNonQuery();
            }
            else
            {
                // Record doesn't exist, use INSERT
                using var insertCommand = connection.CreateCommand();

                insertCommand.CommandText =
                @"
                    INSERT INTO DailyPlaytime
                        (DateID, JobID, Seconds)
                    VALUES
                        ($dateId, $jobId, $seconds);
                ";

                insertCommand.Parameters.AddWithValue("$dateId", dateId);
                insertCommand.Parameters.AddWithValue("$jobId", jobId);
                insertCommand.Parameters.AddWithValue("$seconds", playtime.TotalSeconds);

                insertCommand.ExecuteNonQuery();
            }
        }
    }

    // Get playtime for a specific job on a specific date

    public TimeSpan GetJobPlaytimeForDate(
        DateTime date,
        string jobAbbreviation)
    {
        string dateString = date.ToString("yyyy-MM-dd");

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var command = connection.CreateCommand();

        command.CommandText =
        @"
            SELECT dp.Seconds

            FROM DailyPlaytime dp

            JOIN Dates d
                ON dp.DateID = d.DateID

            JOIN Jobs j
                ON dp.JobID = j.JobID

            WHERE d.Date = $date
              AND j.JobAbbreviation = $abbreviation;
        ";

        command.Parameters.AddWithValue("$date", dateString);
        command.Parameters.AddWithValue(
            "$abbreviation",
            jobAbbreviation);

        var result = command.ExecuteScalar();

        return result != null
            ? TimeSpan.FromSeconds(Convert.ToDouble(result))
            : TimeSpan.Zero;
    }

    // Get total playtime for a specific date

    public TimeSpan GetPlaytimeForDate(DateTime date)
    {
        string dateString = date.ToString("yyyy-MM-dd");

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var command = connection.CreateCommand();

        command.CommandText =
        @"
            SELECT COALESCE(SUM(dp.Seconds), 0)

            FROM DailyPlaytime dp

            JOIN Dates d
                ON dp.DateID = d.DateID

            WHERE d.Date = $date;
        ";

        command.Parameters.AddWithValue("$date", dateString);

        var result = command.ExecuteScalar();

        return TimeSpan.FromSeconds(
            Convert.ToDouble(result));
    }

    // Get total playtime for all dates
    public Dictionary<DateTime, TimeSpan> GetAllPlaytime()
    {
        var results = new Dictionary<DateTime, TimeSpan>();

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var command = connection.CreateCommand();

        command.CommandText =
        @"
            SELECT
                d.Date,
                COALESCE(SUM(dp.Seconds), 0)

            FROM Dates d

            LEFT JOIN DailyPlaytime dp
                ON d.DateID = dp.DateID

            GROUP BY d.DateID, d.Date

            ORDER BY d.Date;
        ";

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var date = DateTime.Parse(
                reader.GetString(0));

            var seconds = reader.GetDouble(1);

            results[date] =
                TimeSpan.FromSeconds(seconds);
        }

        return results;
    }

    // Get playtime by job for a specific date
    public Dictionary<string, TimeSpan> GetPlaytimeByJob(
        DateTime date)
    {
        var results = new Dictionary<string, TimeSpan>();

        string dateString = date.ToString("yyyy-MM-dd");

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var command = connection.CreateCommand();

        command.CommandText =
        @"
            SELECT
                j.JobAbbreviation,
                dp.Seconds

            FROM DailyPlaytime dp

            JOIN Dates d
                ON dp.DateID = d.DateID

            JOIN Jobs j
                ON dp.JobID = j.JobID

            WHERE d.Date = $date

            ORDER BY dp.Seconds DESC;
        ";

        command.Parameters.AddWithValue("$date", dateString);

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            string job = reader.GetString(0);
            double seconds = reader.GetDouble(1);

            results[job] =
                TimeSpan.FromSeconds(seconds);
        }

        return results;
    }

    public Dictionary<string, (string Name, TimeSpan Playtime)> GetPlaytimeByJobWithNames(
        DateTime date)
    {
        var results = new Dictionary<string, (string Name, TimeSpan Playtime)>();

        string dateString = date.ToString("yyyy-MM-dd");

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var command = connection.CreateCommand();

        command.CommandText =
        @"
            SELECT
                j.JobAbbreviation,
                j.JobName,
                dp.Seconds

            FROM DailyPlaytime dp

            JOIN Dates d
                ON dp.DateID = d.DateID

            JOIN Jobs j
                ON dp.JobID = j.JobID

            WHERE d.Date = $date

            ORDER BY dp.Seconds DESC;
        ";

        command.Parameters.AddWithValue("$date", dateString);

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            string jobAbbreviation = reader.GetString(0);
            string jobName = reader.GetString(1);
            double seconds = reader.GetDouble(2);

            results[jobAbbreviation] =
                (jobName, TimeSpan.FromSeconds(seconds));
        }

        return results;
    }

    
   
}
