using Npgsql;
using NpgsqlTypes;

const string DefaultConnectionString =
    "Host=localhost;Port=5432;Database=cinema_training;Username=postgres;Password=YOUR_PASSWORD";

var connectionString = Environment.GetEnvironmentVariable("CINEMA_CONNECTION_STRING");
if (string.IsNullOrWhiteSpace(connectionString))
{
    connectionString = DefaultConnectionString;
    Console.WriteLine("Переменная CINEMA_CONNECTION_STRING не задана. Откройте Program.cs и укажите строку подключения локально.");
}

await using var connection = new NpgsqlConnection(connectionString);
try
{
    await connection.OpenAsync();
    await using var checkCommand = new NpgsqlCommand("SELECT CURRENT_TIMESTAMP", connection);
    var serverTime = await checkCommand.ExecuteScalarAsync();
    Console.WriteLine($"Подключение к cinema_training выполнено. Время PostgreSQL: {serverTime}");
}
catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException)
{
    Console.WriteLine($"Не удалось подключиться к PostgreSQL: {ex.Message}");
    return;
}

while (true)
{
    PrintMenu();
    var choice = Console.ReadLine()?.Trim();
    Console.WriteLine();

    try
    {
        switch (choice)
        {
            case "1": await ShowFilmsAsync(connection); break;
            case "2": await ShowScheduleAsync(connection); break;
            case "3": await FindCustomerTicketsAsync(connection); break;
            case "4": await RegisterCustomerAsync(connection); break;
            case "5": await BuyTicketAsync(connection); break;
            case "6": await CancelTicketAsync(connection); break;
            case "7": await ShowReportsAsync(connection); break;
            case "0": return;
            default: Console.WriteLine("Неизвестный пункт меню."); break;
        }
    }
    catch (NpgsqlException ex)
    {
        Console.WriteLine($"Ошибка базы данных: {ex.Message}");
    }

    Console.WriteLine("\nНажмите Enter, чтобы продолжить...");
    Console.ReadLine();
}

static void PrintMenu()
{
    Console.Clear();
    Console.WriteLine("=== Продажа билетов в кинотеатре ===");
    Console.WriteLine("1. Фильмы и поиск по названию");
    Console.WriteLine("2. Расписание и свободные места");
    Console.WriteLine("3. Клиент и его билеты");
    Console.WriteLine("4. Регистрация клиента");
    Console.WriteLine("5. Покупка билета");
    Console.WriteLine("6. Отмена билета");
    Console.WriteLine("7. Отчёты");
    Console.WriteLine("0. Выход");
    Console.Write("Выберите пункт: ");
}

static async Task ShowFilmsAsync(NpgsqlConnection connection)
{
    Console.Write("Введите часть названия (Enter - все фильмы): ");
    var search = Console.ReadLine()?.Trim() ?? string.Empty;
    const string sql = """
        SELECT film_id, title, genre, duration_minutes, age_rating, release_year
        FROM films
        WHERE @search = '' OR title ILIKE '%' || @search || '%'
        ORDER BY title;
        """;

    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("search", search);
    await using var reader = await command.ExecuteReaderAsync();

    Console.WriteLine("\nФильмы:");
    var found = false;
    while (await reader.ReadAsync())
    {
        found = true;
        Console.WriteLine($"{reader.GetInt32(0)} | {reader.GetString(1)} | {reader.GetString(2)} | " +
                          $"{reader.GetInt32(3)} мин. | {reader.GetInt32(4)}+ | {reader.GetInt32(5)} г.");
    }
    if (!found) Console.WriteLine("Фильмы не найдены.");
}

static async Task ShowScheduleAsync(NpgsqlConnection connection)
{
    Console.Write("Дата от (ГГГГ-ММ-ДД, Enter - без фильтра): ");
    var fromText = Console.ReadLine()?.Trim();
    Console.Write("Дата до (ГГГГ-ММ-ДД, Enter - без фильтра): ");
    var toText = Console.ReadLine()?.Trim();

    if (!TryParseOptionalDate(fromText, "начала", out var from) ||
        !TryParseOptionalDate(toText, "окончания", out var to))
        return;
    if (from.HasValue && to.HasValue && from > to)
    {
        Console.WriteLine("Дата начала не может быть позже даты окончания.");
        return;
    }

    const string sql = """
        SELECT s.screening_id, f.title, f.genre, s.starts_at, h.hall_name,
               s.ticket_price, h.seat_count,
               h.seat_count - COUNT(t.ticket_id) FILTER (WHERE t.status = 'Purchased') AS free_seats
        FROM screenings s
        JOIN films f ON f.film_id = s.film_id
        JOIN halls h ON h.hall_id = s.hall_id
        LEFT JOIN tickets t ON t.screening_id = s.screening_id
        WHERE (@from_date IS NULL OR s.starts_at >= @from_date)
          AND (@to_date IS NULL OR s.starts_at < @to_date + INTERVAL '1 day')
        GROUP BY s.screening_id, f.title, f.genre, s.starts_at, h.hall_name,
                 s.ticket_price, h.seat_count
        ORDER BY s.starts_at;
        """;

    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("from_date", NpgsqlDbType.Timestamp, (object?)from ?? DBNull.Value);
    command.Parameters.AddWithValue("to_date", NpgsqlDbType.Timestamp, (object?)to ?? DBNull.Value);
    await using var reader = await command.ExecuteReaderAsync();

    Console.WriteLine("\nРасписание:");
    while (await reader.ReadAsync())
    {
        Console.WriteLine($"Сеанс {reader.GetInt32(0)} | {reader.GetString(1)} ({reader.GetString(2)}) | " +
                          $"{reader.GetDateTime(3):dd.MM.yyyy HH:mm} | {reader.GetString(4)} | " +
                          $"{reader.GetDecimal(5):0.00} | мест: {reader.GetInt32(6)}, свободно: {reader.GetInt64(7)}");
    }
}

static async Task FindCustomerTicketsAsync(NpgsqlConnection connection)
{
    Console.Write("Email клиента: ");
    var email = Console.ReadLine()?.Trim() ?? string.Empty;
    const string sql = """
        SELECT c.first_name, c.last_name, f.title, s.starts_at, h.hall_name,
               st.seat_number, s.ticket_price, t.status, t.ticket_id
        FROM customers c
        LEFT JOIN tickets t ON t.customer_id = c.customer_id
        LEFT JOIN screenings s ON s.screening_id = t.screening_id
        LEFT JOIN films f ON f.film_id = s.film_id
        LEFT JOIN halls h ON h.hall_id = s.hall_id
        LEFT JOIN seats st ON st.seat_id = t.seat_id
        WHERE c.email = @email
        ORDER BY s.starts_at NULLS LAST, t.ticket_id;
        """;

    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("email", email);
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        Console.WriteLine("Клиент с таким email не найден.");
        return;
    }

    Console.WriteLine($"Клиент: {reader.GetString(0)} {reader.GetString(1)}");
    if (reader.IsDBNull(2))
    {
        Console.WriteLine("Билетов нет.");
        return;
    }

    do
    {
        Console.WriteLine($"Билет {reader.GetInt32(8)} | {reader.GetString(2)} | " +
                          $"{reader.GetDateTime(3):dd.MM.yyyy HH:mm} | {reader.GetString(4)} | " +
                          $"место {reader.GetInt32(5)} | {reader.GetDecimal(6):0.00} | {reader.GetString(7)}");
    } while (await reader.ReadAsync());
}

static async Task RegisterCustomerAsync(NpgsqlConnection connection)
{
    var firstName = ReadRequired("Имя: ");
    var lastName = ReadRequired("Фамилия: ");
    var email = ReadRequired("Email: ");
    Console.Write("Телефон (необязательно): ");
    var phone = Console.ReadLine()?.Trim() ?? string.Empty;

    const string sql = """
        INSERT INTO customers (first_name, last_name, email, phone)
        VALUES (@first_name, @last_name, @email, NULLIF(@phone, ''))
        RETURNING customer_id;
        """;
    try
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("first_name", firstName);
        command.Parameters.AddWithValue("last_name", lastName);
        command.Parameters.AddWithValue("email", email);
        command.Parameters.AddWithValue("phone", phone);
        var id = await command.ExecuteScalarAsync();
        Console.WriteLine($"Клиент зарегистрирован. ID: {id}");
    }
    catch (PostgresException ex) when (ex.SqlState == "23505")
    {
        Console.WriteLine("Клиент с таким email уже существует.");
    }
}

static async Task BuyTicketAsync(NpgsqlConnection connection)
{
    var screeningId = ReadInt("ID сеанса: ");
    var customerId = ReadInt("ID клиента: ");
    var seatNumber = ReadInt("Номер места: ");

    await using var transaction = await connection.BeginTransactionAsync();
    try
    {
        const string sql = """
            WITH selected AS (
                SELECT s.screening_id, s.hall_id, s.ticket_price, s.starts_at,
                       f.title, st.seat_id, st.seat_number
                FROM screenings s
                JOIN films f ON f.film_id = s.film_id
                JOIN seats st ON st.hall_id = s.hall_id AND st.seat_number = @seat_number
                WHERE s.screening_id = @screening_id
                  AND EXISTS (SELECT 1 FROM customers WHERE customer_id = @customer_id)
                  AND NOT EXISTS (
                      SELECT 1 FROM tickets t
                      WHERE t.screening_id = s.screening_id
                        AND t.seat_id = st.seat_id
                        AND t.status = 'Purchased'
                  )
            )
            INSERT INTO tickets (screening_id, hall_id, seat_id, customer_id, status)
            SELECT screening_id, hall_id, seat_id, @customer_id, 'Purchased'
            FROM selected
            RETURNING ticket_id, title, starts_at, seat_number, ticket_price;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("screening_id", screeningId);
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue("seat_number", seatNumber);
        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            await transaction.RollbackAsync();
            Console.WriteLine("Покупка невозможна: проверьте ID сеанса, ID клиента, номер места и его доступность.");
            return;
        }

        var ticketId = reader.GetInt32(0);
        var title = reader.GetString(1);
        var startsAt = reader.GetDateTime(2);
        var purchasedSeat = reader.GetInt32(3);
        var price = reader.GetDecimal(4);
        await reader.CloseAsync();
        await transaction.CommitAsync();
        Console.WriteLine($"Билет куплен: №{ticketId}, {title}, {startsAt:dd.MM.yyyy HH:mm}, место {purchasedSeat}, {price:0.00}.");
    }
    catch (PostgresException ex) when (ex.SqlState == "23505")
    {
        await transaction.RollbackAsync();
        Console.WriteLine("Место уже занято другим пользователем.");
    }
    catch
    {
        await transaction.RollbackAsync();
        throw;
    }
}

static async Task CancelTicketAsync(NpgsqlConnection connection)
{
    var ticketId = ReadInt("Номер билета: ");
    const string sql = """
        UPDATE tickets
        SET status = 'Cancelled'
        WHERE ticket_id = @ticket_id AND status = 'Purchased';
        """;
    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("ticket_id", ticketId);
    var affected = await command.ExecuteNonQueryAsync();
    Console.WriteLine(affected == 1
        ? "Билет отменён, место снова свободно."
        : "Билет не найден или уже отменён.");
}

static async Task ShowReportsAsync(NpgsqlConnection connection)
{
    const string reportSql = """
        SELECT f.title, s.starts_at, h.hall_name,
               COUNT(t.ticket_id) FILTER (WHERE t.status = 'Purchased') AS sold,
               COALESCE(SUM(tickets_for_sum.ticket_price), 0::numeric) AS revenue
        FROM screenings s
        JOIN films f ON f.film_id = s.film_id
        JOIN halls h ON h.hall_id = s.hall_id
        LEFT JOIN tickets t ON t.screening_id = s.screening_id
        LEFT JOIN LATERAL (
            SELECT s.ticket_price
            WHERE t.status = 'Purchased'
        ) tickets_for_sum ON TRUE
        GROUP BY f.title, s.starts_at, h.hall_name
        ORDER BY s.starts_at;
        """;

    await using (var command = new NpgsqlCommand(reportSql, connection))
    await using (var reader = await command.ExecuteReaderAsync())
    {
        Console.WriteLine("Отчёт по сеансам:");
        while (await reader.ReadAsync())
            Console.WriteLine($"{reader.GetString(0)} | {reader.GetDateTime(1):dd.MM.yyyy HH:mm} | {reader.GetString(2)} | " +
                              $"продано: {reader.GetInt64(3)} | выручка: {reader.GetDecimal(4):0.00}");
    }

    const string topSql = """
        SELECT f.title, COUNT(t.ticket_id) AS sold
        FROM tickets t
        JOIN screenings s ON s.screening_id = t.screening_id
        JOIN films f ON f.film_id = s.film_id
        WHERE t.status = 'Purchased'
        GROUP BY f.film_id, f.title
        ORDER BY sold DESC, f.title
        LIMIT 3;
        """;
    await using var topCommand = new NpgsqlCommand(topSql, connection);
    await using var topReader = await topCommand.ExecuteReaderAsync();
    Console.WriteLine("\nТоп-3 фильма по проданным билетам:");
    var place = 1;
    while (await topReader.ReadAsync())
        Console.WriteLine($"{place++}. {topReader.GetString(0)} - {topReader.GetInt64(1)} билет(ов)");
}

static bool TryParseOptionalDate(string? value, string label, out DateTime? date)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        date = null;
        return true;
    }

    if (DateTime.TryParse(value, out var parsed))
    {
        date = parsed.Date;
        return true;
    }

    Console.WriteLine($"Некорректная дата {label} фильтра. Используйте формат ГГГГ-ММ-ДД.");
    date = null;
    return false;
}

static string ReadRequired(string prompt)
{
    while (true)
    {
        Console.Write(prompt);
        var value = Console.ReadLine()?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(value)) return value;
        Console.WriteLine("Поле не может быть пустым.");
    }
}

static int ReadInt(string prompt)
{
    while (true)
    {
        Console.Write(prompt);
        if (int.TryParse(Console.ReadLine(), out var value)) return value;
        Console.WriteLine("Введите целое число.");
    }
}
