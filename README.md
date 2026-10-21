# CinemaAdoNet

Консольное приложение для учебной базы кинотеатра на C#, ADO.NET и PostgreSQL.

## Запуск

1. Создайте базу `cinema_training` и выполните файл `cinema_training.sql`.
2. Задайте строку подключения:

```powershell
$env:CINEMA_CONNECTION_STRING = "Host=localhost;Port=5432;Database=cinema_training;Username=postgres;Password=ВАШ_ПАРОЛЬ"
```

3. Запустите проект:

```powershell
dotnet run
```

Пароль не хранится в исходном коде и не должен попадать в Git.
