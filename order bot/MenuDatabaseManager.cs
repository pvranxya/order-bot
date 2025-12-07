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

    // ============ МЕТОД 1: Получить список ресторанов ============
    public List<string> GetRestaurants()
    {
        EnsureConnectionOpen();
        var restaurants = new List<string>();

        using (var command = _connection.CreateCommand())
        {
            command.CommandText = "SELECT DISTINCT Restaurant FROM MenuItems ORDER BY Restaurant";

            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    var restaurant = reader.GetString(0);
                    if (!string.IsNullOrEmpty(restaurant))
                    {
                        restaurants.Add(restaurant);
                    }
                }
            }
        }

        return restaurants;
    }

    // ============ МЕТОД 2: Получить меню по ресторану ============
    public List<MenuItem> GetMenuItemsByRestaurant(string restaurant)
    {
        if (string.IsNullOrWhiteSpace(restaurant))
            throw new ArgumentException("Название ресторана не может быть пустым", nameof(restaurant));

        EnsureConnectionOpen();
        var menuItems = new List<MenuItem>();

        using (var command = _connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM MenuItems WHERE Restaurant = @restaurant ORDER BY Category, Name";
            command.Parameters.AddWithValue("@restaurant", restaurant);

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

    // ============ МЕТОД 3: Получить категории по ресторану ============
    public List<string> GetCategoriesByRestaurant(string restaurant)
    {
        if (string.IsNullOrWhiteSpace(restaurant))
            throw new ArgumentException("Название ресторана не может быть пустым", nameof(restaurant));

        EnsureConnectionOpen();
        var categories = new List<string>();

        using (var command = _connection.CreateCommand())
        {
            command.CommandText = "SELECT DISTINCT Category FROM MenuItems WHERE Restaurant = @restaurant ORDER BY Category";
            command.Parameters.AddWithValue("@restaurant", restaurant);

            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    var category = reader.GetString(0);
                    if (!string.IsNullOrEmpty(category))
                    {
                        categories.Add(category);
                    }
                }
            }
        }

        return categories;
    }

    // ============ МЕТОД 4: Получить блюда по ресторану и категории ============
    public List<MenuItem> GetMenuItemsByRestaurantAndCategory(string restaurant, string category)
    {
        if (string.IsNullOrWhiteSpace(restaurant))
            throw new ArgumentException("Название ресторана не может быть пустым", nameof(restaurant));

        if (string.IsNullOrWhiteSpace(category))
            throw new ArgumentException("Категория не может быть пустой", nameof(category));

        EnsureConnectionOpen();
        var menuItems = new List<MenuItem>();

        using (var command = _connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM MenuItems WHERE Restaurant = @restaurant AND Category = @category ORDER BY Name";
            command.Parameters.AddWithValue("@restaurant", restaurant);
            command.Parameters.AddWithValue("@category", category);

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

    // ============ МЕТОД 5: Показать меню ресторана с группировкой по категориям ============
    public void ShowRestaurantMenuByCategories(string restaurant)
    {
        if (string.IsNullOrWhiteSpace(restaurant))
            throw new ArgumentException("Название ресторана не может быть пустым", nameof(restaurant));

        // Получаем все категории ресторана
        var categories = GetCategoriesByRestaurant(restaurant);

        if (categories.Count == 0)
        {
            Console.WriteLine($"Ресторан '{restaurant}' не найден или меню пусто.");
            return;
        }

        Console.WriteLine($"\n=== МЕНЮ РЕСТОРАНА: {restaurant} ===");
        Console.WriteLine($"Категорий: {categories.Count}");
        Console.WriteLine("=".PadRight(60, '='));

        // Для каждой категории получаем и выводим блюда
        foreach (var category in categories)
        {
            var items = GetMenuItemsByRestaurantAndCategory(restaurant, category);

            if (items.Count > 0)
            {
                Console.WriteLine($"\n{category.ToUpper()}:");
                Console.WriteLine($"{"Название",-50} {"Цена",-10}");
                Console.WriteLine("-".PadRight(60, '-'));

                foreach (var item in items)
                {
                    Console.WriteLine($"{item.Name,-50} {item.Price,-10:C}");
                }
            }
        }

        Console.WriteLine("=".PadRight(60, '='));

        // Показываем итоговую сумму всех блюд
        var allItems = GetMenuItemsByRestaurant(restaurant);
        decimal total = 0;
        foreach (var item in allItems)
        {
            total += item.Price;
        }
        Console.WriteLine($"Всего блюд: {allItems.Count}, Общая сумма: {total:C}");
    }

    // ============ СУЩЕСТВУЮЩИЕ МЕТОДЫ (без изменений) ============
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
            command.CommandText = "SELECT * FROM MenuItems ORDER BY Restaurant, Category, Name";

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
    public void ShowAllDatabase()
    {
        EnsureConnectionOpen();

        // Получаем все рестораны
        var restaurants = GetRestaurants();

        if (restaurants.Count == 0)
        {
            Console.WriteLine("База данных пуста.");
            return;
        }

        Console.WriteLine("\n" + "=".PadRight(90, '='));
        Console.WriteLine("ПОЛНАЯ БАЗА ДАННЫХ МЕНЮ");
        Console.WriteLine("=".PadRight(90, '='));

        int totalItemsCount = 0;
        decimal totalPriceSum = 0;

        // Проходим по всем ресторанам
        foreach (var restaurant in restaurants)
        {
            Console.WriteLine($"\n■ РЕСТОРАН: {restaurant}");
            Console.WriteLine("-".PadRight(90, '-'));

            // Получаем категории для текущего ресторана
            var categories = GetCategoriesByRestaurant(restaurant);

            int restaurantItemsCount = 0;
            decimal restaurantPriceSum = 0;

            // Проходим по всем категориям ресторана
            foreach (var category in categories)
            {
                var items = GetMenuItemsByRestaurantAndCategory(restaurant, category);

                if (items.Count > 0)
                {
                    Console.WriteLine($"\n  Категория: {category}");
                    Console.WriteLine($"  {"Название",-50} {"Цена",-15} {"ID",-10}");
                    Console.WriteLine("  " + "-".PadRight(75, '-'));

                    foreach (var item in items)
                    {
                        Console.WriteLine($"  {item.Name,-50} {item.Price,-15:C} #{item.Id,-8}");
                        restaurantItemsCount++;
                        restaurantPriceSum += item.Price;
                    }
                }
            }

            // Итоги по ресторану
            Console.WriteLine($"\n  Итого по ресторану '{restaurant}':");
            Console.WriteLine($"  • Блюд: {restaurantItemsCount}");
            Console.WriteLine($"  • Общая стоимость: {restaurantPriceSum:C}");
            Console.WriteLine("  " + "~".PadRight(75, '~'));

            totalItemsCount += restaurantItemsCount;
            totalPriceSum += restaurantPriceSum;
        }

        // Общие итоги
        Console.WriteLine("\n" + "=".PadRight(90, '='));
        Console.WriteLine("ОБЩИЕ ИТОГИ:");
        Console.WriteLine($"• Всего ресторанов: {restaurants.Count}");
        Console.WriteLine($"• Всего блюд в базе: {totalItemsCount}");
        Console.WriteLine($"• Общая стоимость всех блюд: {totalPriceSum:C}");
        Console.WriteLine("=".PadRight(90, '='));
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