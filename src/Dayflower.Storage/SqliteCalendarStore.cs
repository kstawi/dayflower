// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using System.Globalization;
using System.Text.Json;
using Dayflower.Core.Model;
using Dayflower.Core.Providers;
using Dayflower.Core.Storage;
using Microsoft.Data.Sqlite;

namespace Dayflower.Storage;

/// <summary>
/// The cache in a SQLite file. Events are kept as the provider's payload plus a few indexed columns (time range,
/// series, iCalendar UID); the model is rebuilt from the payload when read, so mapping improvements apply to
/// cached events without a resync.
/// </summary>
public sealed class SqliteCalendarStore : ICalendarStore
{
    private const int SchemaVersion = 1;

    private readonly string _connectionString;

    private SqliteCalendarStore(string path)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            ForeignKeys = true,
            Pooling = true,
        }.ToString();
    }

    /// <summary>Opens (and if needed creates or upgrades) the cache at <paramref name="path"/>.</summary>
    public static SqliteCalendarStore Open(string path)
    {
        var store = new SqliteCalendarStore(path);
        store.Migrate();
        return store;
    }

    public async Task<IReadOnlyList<AccountRecord>> GetAccountsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = OpenConnection();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT id, provider, email, display_name, sort_order, state, last_synced_utc FROM accounts ORDER BY sort_order";
        var result = new List<AccountRecord>();
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            result.Add(new AccountRecord(
                r.GetString(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3), r.GetInt32(4),
                (AccountState)r.GetInt32(5),
                r.IsDBNull(6) ? null : DateTimeOffset.Parse(r.GetString(6), CultureInfo.InvariantCulture)));
        }

        return result;
    }

    public async Task SaveAccountAsync(AccountRecord account, byte[] protectedSecret, CancellationToken cancellationToken = default)
    {
        await using var db = OpenConnection();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO accounts (id, provider, email, display_name, sort_order, state, secret)
            VALUES ($id, $provider, $email, $name, (SELECT COALESCE(MAX(sort_order) + 1, 0) FROM accounts), $state, $secret)
            ON CONFLICT (id) DO UPDATE SET email = $email, display_name = $name, state = $state, secret = $secret
            """;
        cmd.Parameters.AddWithValue("$id", account.Id);
        cmd.Parameters.AddWithValue("$provider", account.ProviderId);
        cmd.Parameters.AddWithValue("$email", account.Email);
        cmd.Parameters.AddWithValue("$name", (object?)account.DisplayName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$state", (int)account.State);
        cmd.Parameters.AddWithValue("$secret", protectedSecret);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<byte[]?> GetAccountSecretAsync(string accountId, CancellationToken cancellationToken = default) =>
        await ScalarAsync("SELECT secret FROM accounts WHERE id = $id", cancellationToken, ("$id", accountId)) as byte[];

    public Task SetAccountStateAsync(string accountId, AccountState state, CancellationToken cancellationToken = default) =>
        ExecuteAsync("UPDATE accounts SET state = $state WHERE id = $id", cancellationToken, ("$state", (int)state), ("$id", accountId));

    public Task SetAccountSyncedAsync(string accountId, DateTimeOffset syncedUtc, CancellationToken cancellationToken = default) =>
        ExecuteAsync("UPDATE accounts SET last_synced_utc = $at WHERE id = $id", cancellationToken,
                     ("$at", syncedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)), ("$id", accountId));

    public async Task RemoveAccountAsync(string accountId, CancellationToken cancellationToken = default)
    {
        await using var db = OpenConnection();
        await using var tx = await db.BeginTransactionAsync(cancellationToken);
        await ExecuteAsync(db, "DELETE FROM events WHERE account_id = $id", cancellationToken, ("$id", accountId));
        await ExecuteAsync(db, "DELETE FROM calendars WHERE account_id = $id", cancellationToken, ("$id", accountId));
        await ExecuteAsync(db, "DELETE FROM accounts WHERE id = $id", cancellationToken, ("$id", accountId));
        await tx.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CalendarRecord>> GetCalendarsAsync(string? accountId = null, CancellationToken cancellationToken = default)
    {
        await using var db = OpenConnection();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT c.account_id, c.id, c.name, c.time_zone, c.background_color, c.foreground_color, c.access,
                   c.is_primary, c.is_visible, c.default_reminders
            FROM calendars c JOIN accounts a ON a.id = c.account_id
            WHERE $account IS NULL OR c.account_id = $account
            ORDER BY a.sort_order, c.is_primary DESC, c.name COLLATE NOCASE
            """;
        cmd.Parameters.AddWithValue("$account", (object?)accountId ?? DBNull.Value);
        var result = new List<CalendarRecord>();
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            result.Add(new CalendarRecord(
                r.GetString(0), r.GetString(1), r.GetString(2), NullableString(r, 3), NullableString(r, 4), NullableString(r, 5),
                (CalendarAccess)r.GetInt32(6), r.GetBoolean(7), r.GetBoolean(8),
                JsonSerializer.Deserialize<List<Reminder>>(r.GetString(9)) ?? []));
        }

        return result;
    }

    public async Task<string?> GetCalendarListSyncTokenAsync(string accountId, CancellationToken cancellationToken = default) =>
        await ScalarAsync("SELECT calendar_list_sync_token FROM accounts WHERE id = $id", cancellationToken, ("$id", accountId)) as string;

    public async Task ApplyCalendarChangesAsync(string accountId, CalendarListChanges changes, CancellationToken cancellationToken = default)
    {
        await using var db = OpenConnection();
        await using var tx = await db.BeginTransactionAsync(cancellationToken);

        var removed = new HashSet<string>(changes.RemovedIds);
        if (changes.IsFullSync)
        {
            var listed = changes.Changed.Select(c => c.Id).ToHashSet();
            foreach (var calendar in await GetCalendarIdsAsync(db, accountId, cancellationToken))
            {
                if (!listed.Contains(calendar))
                {
                    removed.Add(calendar);
                }
            }
        }

        foreach (var id in removed)
        {
            await ExecuteAsync(db, "DELETE FROM events WHERE account_id = $a AND calendar_id = $c", cancellationToken, ("$a", accountId), ("$c", id));
            await ExecuteAsync(db, "DELETE FROM calendars WHERE account_id = $a AND id = $c", cancellationToken, ("$a", accountId), ("$c", id));
        }

        foreach (var c in changes.Changed)
        {
            // The user's show/hide choice (is_visible) and the event sync token survive updates.
            await ExecuteAsync(db, """
                INSERT INTO calendars (account_id, id, name, time_zone, background_color, foreground_color, access,
                                       is_primary, is_visible, default_reminders)
                VALUES ($a, $id, $name, $tz, $bg, $fg, $access, $primary, $visible, $reminders)
                ON CONFLICT (account_id, id) DO UPDATE SET
                    name = $name, time_zone = $tz, background_color = $bg, foreground_color = $fg, access = $access,
                    is_primary = $primary, default_reminders = $reminders
                """, cancellationToken,
                ("$a", accountId), ("$id", c.Id), ("$name", c.Name), ("$tz", c.TimeZone), ("$bg", c.BackgroundColor),
                ("$fg", c.ForegroundColor), ("$access", (int)c.Access), ("$primary", c.IsPrimary), ("$visible", c.IsVisible),
                ("$reminders", JsonSerializer.Serialize(c.DefaultReminders)));
        }

        await ExecuteAsync(db, "UPDATE accounts SET calendar_list_sync_token = $t WHERE id = $a", cancellationToken,
                           ("$t", changes.NextSyncToken), ("$a", accountId));
        await tx.CommitAsync(cancellationToken);
    }

    public Task SetCalendarVisibleAsync(string accountId, string calendarId, bool visible, CancellationToken cancellationToken = default) =>
        ExecuteAsync("UPDATE calendars SET is_visible = $v WHERE account_id = $a AND id = $c", cancellationToken,
                     ("$v", visible), ("$a", accountId), ("$c", calendarId));

    public async Task<string?> GetEventSyncTokenAsync(string accountId, string calendarId, CancellationToken cancellationToken = default) =>
        await ScalarAsync("SELECT sync_token FROM calendars WHERE account_id = $a AND id = $c", cancellationToken,
                          ("$a", accountId), ("$c", calendarId)) as string;

    public async Task ApplyEventChangesAsync(string accountId, string calendarId, EventChangeSet changes, CancellationToken cancellationToken = default)
    {
        string provider = accountId.Split(':')[0];
        await using var db = OpenConnection();
        await using var tx = await db.BeginTransactionAsync(cancellationToken);

        if (changes.IsFullSync)
        {
            await ExecuteAsync(db, "DELETE FROM events WHERE account_id = $a AND calendar_id = $c", cancellationToken,
                               ("$a", accountId), ("$c", calendarId));
        }

        await using var upsert = db.CreateCommand();
        upsert.CommandText = """
            INSERT OR REPLACE INTO events (account_id, calendar_id, id, provider, status, ical_uid, series_id, is_series,
                                           range_start, range_end, original_start, payload)
            VALUES ($a, $c, $id, $provider, $status, $uid, $series, $isSeries, $start, $end, $original, $payload)
            """;
        var p = new[] { "$a", "$c", "$id", "$provider", "$status", "$uid", "$series", "$isSeries", "$start", "$end", "$original", "$payload" }
            .ToDictionary(n => n, n => AddParameter(upsert, n));

        await using var remove = db.CreateCommand();
        remove.CommandText = "DELETE FROM events WHERE account_id = $a AND calendar_id = $c AND (id = $id OR series_id = $id)";
        var rp = new[] { "$a", "$c", "$id" }.ToDictionary(n => n, n => AddParameter(remove, n));

        foreach (var change in changes.Changes)
        {
            if (change.Event is not { } e)
            {
                // A removed series takes its changed and cancelled occurrences with it.
                rp["$a"].Value = accountId;
                rp["$c"].Value = calendarId;
                rp["$id"].Value = change.EventId;
                await remove.ExecuteNonQueryAsync(cancellationToken);
                continue;
            }

            p["$a"].Value = accountId;
            p["$c"].Value = calendarId;
            p["$id"].Value = change.EventId;
            p["$provider"].Value = provider;
            p["$status"].Value = (int)e.Status;
            p["$uid"].Value = (object?)e.ICalUid ?? DBNull.Value;
            p["$series"].Value = (object?)e.RecurringEventId ?? DBNull.Value;
            p["$isSeries"].Value = e.IsSeries;
            p["$start"].Value = e.Start.EarliestUtc.ToUnixTimeSeconds();
            p["$end"].Value = e.IsSeries ? DBNull.Value : e.End.LatestUtc.ToUnixTimeSeconds();
            p["$original"].Value = e.OriginalStart is { } o ? o.EarliestUtc.ToUnixTimeSeconds() : DBNull.Value;
            p["$payload"].Value = change.Payload ?? throw new ArgumentException($"Event {change.EventId} has no payload.");
            await upsert.ExecuteNonQueryAsync(cancellationToken);
        }

        await ExecuteAsync(db, "UPDATE calendars SET sync_token = $t WHERE account_id = $a AND id = $c", cancellationToken,
                           ("$t", changes.NextSyncToken), ("$a", accountId), ("$c", calendarId));
        await tx.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StoredEvent>> GetEventsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        await using var db = OpenConnection();
        await using var cmd = db.CreateCommand();

        // One-off events overlapping the range, every series that started before its end, and changed or cancelled
        // occurrences originally scheduled in it (they hide the series' occurrence even if moved elsewhere).
        cmd.CommandText = """
            SELECT account_id, calendar_id, id, provider, payload FROM events
            WHERE (is_series = 0 AND range_start < $to AND range_end >= $from)
               OR (is_series = 1 AND range_start < $to)
               OR (original_start IS NOT NULL AND original_start >= $fromWide AND original_start < $toWide)
            """;
        cmd.Parameters.AddWithValue("$from", from.ToUnixTimeSeconds());
        cmd.Parameters.AddWithValue("$to", to.ToUnixTimeSeconds());
        cmd.Parameters.AddWithValue("$fromWide", from.AddHours(-28).ToUnixTimeSeconds());
        cmd.Parameters.AddWithValue("$toWide", to.AddHours(28).ToUnixTimeSeconds());
        return await ReadEventsAsync(cmd, cancellationToken);
    }

    public async Task<IReadOnlyList<StoredEvent>> GetOccurrenceOverridesAsync(EventKey series, CancellationToken cancellationToken = default)
    {
        await using var db = OpenConnection();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT account_id, calendar_id, id, provider, payload FROM events
            WHERE account_id = $a AND calendar_id = $c AND series_id = $s
            """;
        cmd.Parameters.AddWithValue("$a", series.AccountId);
        cmd.Parameters.AddWithValue("$c", series.CalendarId);
        cmd.Parameters.AddWithValue("$s", series.EventId);
        return await ReadEventsAsync(cmd, cancellationToken);
    }

    public async Task<CacheStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = OpenConnection();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT (SELECT COUNT(*) FROM accounts), (SELECT COUNT(*) FROM calendars), (SELECT COUNT(*) FROM events),
                   (SELECT COUNT(*) FROM events WHERE is_series = 1), (SELECT COUNT(*) FROM events WHERE series_id IS NOT NULL)
            """;
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        await r.ReadAsync(cancellationToken);
        return new CacheStats(r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.GetInt32(3), r.GetInt32(4));
    }

    private void Migrate()
    {
        using var db = OpenConnection();
        using var version = db.CreateCommand();
        version.CommandText = "PRAGMA user_version";
        long current = (long)version.ExecuteScalar()!;
        if (current >= SchemaVersion)
        {
            return;
        }

        using var cmd = db.CreateCommand();
        cmd.CommandText = $"""
            PRAGMA journal_mode = WAL;

            CREATE TABLE IF NOT EXISTS accounts (
                id TEXT PRIMARY KEY,
                provider TEXT NOT NULL,
                email TEXT NOT NULL,
                display_name TEXT,
                sort_order INTEGER NOT NULL,
                state INTEGER NOT NULL,
                secret BLOB NOT NULL,
                calendar_list_sync_token TEXT,
                last_synced_utc TEXT
            );

            CREATE TABLE IF NOT EXISTS calendars (
                account_id TEXT NOT NULL REFERENCES accounts (id) ON DELETE CASCADE,
                id TEXT NOT NULL,
                name TEXT NOT NULL,
                time_zone TEXT,
                background_color TEXT,
                foreground_color TEXT,
                access INTEGER NOT NULL,
                is_primary INTEGER NOT NULL,
                is_visible INTEGER NOT NULL,
                default_reminders TEXT NOT NULL,
                sync_token TEXT,
                PRIMARY KEY (account_id, id)
            );

            -- range_start/range_end/original_start are Unix seconds, widened by 14 hours for all-day events.
            -- range_end is NULL for a series: it may repeat forever.
            CREATE TABLE IF NOT EXISTS events (
                account_id TEXT NOT NULL,
                calendar_id TEXT NOT NULL,
                id TEXT NOT NULL,
                provider TEXT NOT NULL,
                status INTEGER NOT NULL,
                ical_uid TEXT,
                series_id TEXT,
                is_series INTEGER NOT NULL,
                range_start INTEGER NOT NULL,
                range_end INTEGER,
                original_start INTEGER,
                payload TEXT NOT NULL,
                PRIMARY KEY (account_id, calendar_id, id),
                FOREIGN KEY (account_id, calendar_id) REFERENCES calendars (account_id, id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS events_by_range ON events (range_start, range_end);
            CREATE INDEX IF NOT EXISTS events_by_series ON events (account_id, calendar_id, series_id);
            CREATE INDEX IF NOT EXISTS events_by_original_start ON events (original_start);
            CREATE INDEX IF NOT EXISTS events_by_uid ON events (ical_uid);

            PRAGMA user_version = {SchemaVersion};
            """;
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection()
    {
        var db = new SqliteConnection(_connectionString);
        db.Open();
        return db;
    }

    private static async Task<List<string>> GetCalendarIdsAsync(SqliteConnection db, string accountId, CancellationToken cancellationToken)
    {
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT id FROM calendars WHERE account_id = $a";
        cmd.Parameters.AddWithValue("$a", accountId);
        var ids = new List<string>();
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            ids.Add(r.GetString(0));
        }

        return ids;
    }

    private static async Task<IReadOnlyList<StoredEvent>> ReadEventsAsync(SqliteCommand cmd, CancellationToken cancellationToken)
    {
        var result = new List<StoredEvent>();
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            result.Add(new StoredEvent(new EventKey(r.GetString(0), r.GetString(1), r.GetString(2)), r.GetString(3), r.GetString(4)));
        }

        return result;
    }

    private async Task<object?> ScalarAsync(string sql, CancellationToken cancellationToken, params (string Name, object? Value)[] parameters)
    {
        await using var db = OpenConnection();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        AddParameters(cmd, parameters);
        var value = await cmd.ExecuteScalarAsync(cancellationToken);
        return value is DBNull ? null : value;
    }

    private async Task ExecuteAsync(string sql, CancellationToken cancellationToken, params (string Name, object? Value)[] parameters)
    {
        await using var db = OpenConnection();
        await ExecuteAsync(db, sql, cancellationToken, parameters);
    }

    private static async Task ExecuteAsync(SqliteConnection db, string sql, CancellationToken cancellationToken,
                                           params (string Name, object? Value)[] parameters)
    {
        await using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        AddParameters(cmd, parameters);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddParameters(SqliteCommand cmd, (string Name, object? Value)[] parameters)
    {
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
    }

    private static string? NullableString(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    /// <summary>A parameter whose SQLite type follows the value it's given each time.</summary>
    private static SqliteParameter AddParameter(SqliteCommand cmd, string name)
    {
        var parameter = cmd.CreateParameter();
        parameter.ParameterName = name;
        cmd.Parameters.Add(parameter);
        return parameter;
    }
}
