using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using System.IO;

public class MenuDatabaseManager : IDisposable
{
    private SqliteConnection _connection;
    private bool _disposed = false;

    public MenuDatabaseManager(string databasePath = "..\\..\\..\\Databases\\menuitems.db")
    {
        InitializeDatabase(databasePath);
    }

    private void InitializeDatabase(string databasePath)
    {
        try
        {
            // СОЗДАЕМ ПАПКУ ЕСЛИ ЕЕ НЕТ
            var directory = Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                Console.WriteLine($"Создана папка для базы данных: {directory}");
            }

            var connectionString = $"Data Source={databasePath}";
            _connection = new SqliteConnection(connectionString);
            _connection.Open();

            Console.WriteLine($"База данных подключена: {databasePath}");

            using (var command = _connection.CreateCommand())
            {
                command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS MenuItems (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Restaurant TEXT NOT NULL,
                        Name TEXT NOT NULL,
                        Price DECIMAL(10,2) NOT NULL,
                        Category TEXT NOT NULL
                    )";
                command.ExecuteNonQuery();
                Console.WriteLine("Таблица MenuItems создана/проверена");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка при инициализации базы данных: {ex.Message}");
            throw;
        }
    }

    public void ClearAllMenuItem()
    {
        EnsureConnectionOpen();

        using (var command = _connection.CreateCommand())
        {
            command.CommandText = "DELETE FROM MenuItems";
            command.ExecuteNonQuery();
        }

        using (var command = _connection.CreateCommand())
        {
            command.CommandText = "DELETE FROM sqlite_sequence WHERE name='MenuItems'";
            command.ExecuteNonQuery();
        }
    }

    public List<MenuItem> GetAllMenuItem()
    {
        EnsureConnectionOpen();
        var menuItems = new List<MenuItem>();

        using (var command = _connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM MenuItems ORDER BY Id";

            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    var menuItem = new MenuItem
                    {
                        Id = reader.GetInt32(0),
                        Restaurant = reader.GetString(1),
                        Name = reader.GetString(2),
                        Price = reader.GetDecimal(3),
                        Category = reader.GetString(4)
                    };
                    menuItems.Add(menuItem);
                }
            }
        }

        return menuItems;
    }


    public void AddMenuItem(MenuItem menuItem)
    {
        if (menuItem == null)
            throw new ArgumentNullException(nameof(menuItem));

        EnsureConnectionOpen();

        using (var command = _connection.CreateCommand())
        {
            command.CommandText = @"
                INSERT INTO MenuItems (Restaurant, Name, Price, Category)
                VALUES (@restaurant, @name, @price, @category)";

            command.Parameters.AddWithValue("@restaurant", menuItem.Restaurant);
            command.Parameters.AddWithValue("@name", menuItem.Name);
            command.Parameters.AddWithValue("@price", menuItem.Price);
            command.Parameters.AddWithValue("@category", menuItem.Category);

            command.ExecuteNonQuery();

            command.CommandText = "SELECT last_insert_rowid()";
            menuItem.Id = Convert.ToInt32(command.ExecuteScalar());
        }
    }
    public void ShowMenuItems()
    {
        var menuItems = GetAllMenuItem();

        if (menuItems.Count == 0)
        {
            Console.WriteLine("Меню пусто.");
            return;
        }

        Console.WriteLine("\n=== ТЕКУЩЕЕ МЕНЮ ===");
        Console.WriteLine($"Всего блюд: {menuItems.Count}");
        Console.WriteLine("=".PadRight(80, '='));
        Console.WriteLine($"{"ID",-5} {"Ресторан",-20} {"Название",-25} {"Цена",-10} {"Категория",-15}");
        Console.WriteLine("-".PadRight(80, '-'));

        foreach (var item in menuItems)
        {
            Console.WriteLine($"{item.Id,-5} {item.Restaurant,-20} {item.Name,-25} {item.Price,-10:C} {item.Category,-15}");
        }
        Console.WriteLine("=".PadRight(80, '='));
    }
    public MenuItem GetMenuItemById(int id)
    {
        EnsureConnectionOpen();

        using (var command = _connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM MenuItems WHERE Id = @id";
            command.Parameters.AddWithValue("@id", id);

            using (var reader = command.ExecuteReader())
            {
                if (reader.Read())
                {
                    return new MenuItem
                    {
                        Id = reader.GetInt32(0),
                        Restaurant = reader.GetString(1),
                        Name = reader.GetString(2),
                        Price = reader.GetDecimal(3),
                        Category = reader.GetString(4)
                    };
                }
            }
        }

        return null;
    }

    public bool DeleteMenuItem(int id)
    {
        EnsureConnectionOpen();

        using (var command = _connection.CreateCommand())
        {
            command.CommandText = "DELETE FROM MenuItems WHERE Id = @id";
            command.Parameters.AddWithValue("@id", id);

            return command.ExecuteNonQuery() > 0;
        }
    }

    public bool UpdateMenuItem(MenuItem menuItem)
    {
        if (menuItem == null)
            throw new ArgumentNullException(nameof(menuItem));

        EnsureConnectionOpen();

        using (var command = _connection.CreateCommand())
        {
            command.CommandText = @"
                UPDATE MenuItems 
                SET Restaurant = @restaurant, 
                    Name = @name, 
                    Price = @price, 
                    Category = @category
                WHERE Id = @id";

            command.Parameters.AddWithValue("@restaurant", menuItem.Restaurant);
            command.Parameters.AddWithValue("@name", menuItem.Name);
            command.Parameters.AddWithValue("@price", menuItem.Price);
            command.Parameters.AddWithValue("@category", menuItem.Category);
            command.Parameters.AddWithValue("@id", menuItem.Id);

            return command.ExecuteNonQuery() > 0;
        }
    }

    private void EnsureConnectionOpen()
    {
        if (_connection.State != System.Data.ConnectionState.Open)
        {
            _connection.Open();
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                if (_connection != null)
                {
                    _connection.Close();
                    _connection.Dispose();
                    _connection = null;
                }
            }
            _disposed = true;
        }
    }

    ~MenuDatabaseManager()
    {
        Dispose(false);
    }
}