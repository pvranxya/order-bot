using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using System.IO;
using System.Linq;

namespace order_bot
{
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

        // ============ МЕТОД: Проверить и создать ресторан ============
        public bool CheckAndCreateRestaurant(string restaurantName)
        {
            if (string.IsNullOrWhiteSpace(restaurantName))
                return false;

            EnsureConnectionOpen();

            try
            {
                // Проверяем, существует ли уже такой ресторан
                using (var checkCommand = _connection.CreateCommand())
                {
                    checkCommand.CommandText = "SELECT COUNT(*) FROM MenuItems WHERE Restaurant = @restaurant";
                    checkCommand.Parameters.AddWithValue("@restaurant", restaurantName);
                    var count = Convert.ToInt32(checkCommand.ExecuteScalar());

                    if (count > 0)
                    {
                        // Ресторан уже существует
                        Console.WriteLine($"Ресторан '{restaurantName}' уже существует");
                        return true;
                    }
                }

                // Создаем новый ресторан с ПРИМЕРОМ блюда (чтобы ресторан появился в списке)
                using (var command = _connection.CreateCommand())
                {
                    command.CommandText = @"
                        INSERT INTO MenuItems (Restaurant, Name, Price, Category)
                        VALUES (@restaurant, @name, @price, @category)";

                    command.Parameters.AddWithValue("@restaurant", restaurantName);
                    command.Parameters.AddWithValue("@name", "[ПЕРВОЕ БЛЮДО - добавьте через меню]");
                    command.Parameters.AddWithValue("@price", 0.0m);
                    command.Parameters.AddWithValue("@category", "Основные блюда");

                    command.ExecuteNonQuery();
                    Console.WriteLine($"Создан новый ресторан '{restaurantName}' с примерным блюдом");
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при создании ресторана '{restaurantName}': {ex.Message}");
                return false;
            }
        }

        // ============ МЕТОД: Получить список ресторанов ============
        public List<string> GetRestaurants()
        {
            EnsureConnectionOpen();
            var restaurants = new List<string>();

            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "SELECT DISTINCT Restaurant FROM MenuItems WHERE Name != '[ПЕРВОЕ БЛЮДО - добавьте через меню]' OR Price > 0 ORDER BY Restaurant";

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var restaurant = reader.GetString(0);
                        if (!string.IsNullOrEmpty(restaurant) && !restaurants.Contains(restaurant))
                        {
                            restaurants.Add(restaurant);
                        }
                    }
                }
            }

            return restaurants.Distinct().ToList();
        }

        // ============ МЕТОД: Получить меню по ресторану ============
        public List<MenuItem> GetMenuItemsByRestaurant(string restaurant)
        {
            if (string.IsNullOrWhiteSpace(restaurant))
                throw new ArgumentException("Название ресторана не может быть пустым", nameof(restaurant));

            EnsureConnectionOpen();
            var menuItems = new List<MenuItem>();

            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM MenuItems WHERE Restaurant = @restaurant AND Name != '[ПЕРВОЕ БЛЮДО - добавьте через меню]' ORDER BY Category, Name";
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

                        // Пропускаем служебные записи
                        if (menuItem.Name != "[ПЕРВОЕ БЛЮДО - добавьте через меню]" || menuItem.Price > 0)
                        {
                            menuItems.Add(menuItem);
                        }
                    }
                }
            }

            return menuItems;
        }

        // ============ МЕТОД: Получить категории по ресторану ============
        public List<string> GetCategoriesByRestaurant(string restaurant)
        {
            if (string.IsNullOrWhiteSpace(restaurant))
                throw new ArgumentException("Название ресторана не может быть пустым", nameof(restaurant));

            EnsureConnectionOpen();
            var categories = new List<string>();

            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "SELECT DISTINCT Category FROM MenuItems WHERE Restaurant = @restaurant AND Name != '[ПЕРВОЕ БЛЮДО - добавьте через меню]' ORDER BY Category";
                command.Parameters.AddWithValue("@restaurant", restaurant);

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var category = reader.GetString(0);
                        if (!string.IsNullOrEmpty(category) && !categories.Contains(category))
                        {
                            categories.Add(category);
                        }
                    }
                }
            }

            return categories.Distinct().ToList();
        }

        // ============ МЕТОД: Получить блюда по ресторану и категории ============
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
                command.CommandText = "SELECT * FROM MenuItems WHERE Restaurant = @restaurant AND Category = @category AND Name != '[ПЕРВОЕ БЛЮДО - добавьте через меню]' ORDER BY Name";
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

                        if (menuItem.Name != "[ПЕРВОЕ БЛЮДО - добавьте через меню]")
                        {
                            menuItems.Add(menuItem);
                        }
                    }
                }
            }

            return menuItems;
        }

        // ============ МЕТОД: Добавить ресторан (упрощенный) ============
        public bool AddRestaurant(string restaurantName)
        {
            return CheckAndCreateRestaurant(restaurantName);
        }

        // ============ МЕТОД: Добавить блюдо в меню ============
        public bool AddMenuItem(string restaurant, string name, decimal price, string category)
        {
            if (string.IsNullOrWhiteSpace(restaurant))
            {
                Console.WriteLine("Ошибка: название ресторана пустое");
                return false;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                Console.WriteLine("Ошибка: название блюда пустое");
                return false;
            }

            if (string.IsNullOrWhiteSpace(category))
            {
                Console.WriteLine("Ошибка: категория пустая");
                return false;
            }

            if (price < 0)
            {
                Console.WriteLine("Ошибка: цена не может быть отрицательной");
                return false;
            }

            try
            {
                EnsureConnectionOpen();

                // Сначала проверяем, существует ли ресторан
                var restaurants = GetRestaurants();
                if (!restaurants.Contains(restaurant))
                {
                    // Если ресторана нет - создаем его
                    Console.WriteLine($"Ресторан '{restaurant}' не найден, создаем...");
                    if (!CheckAndCreateRestaurant(restaurant))
                    {
                        Console.WriteLine($"Не удалось создать ресторан '{restaurant}'");
                        return false;
                    }
                }

                // Проверяем, нет ли уже такого блюда в этой категории
                using (var checkCommand = _connection.CreateCommand())
                {
                    checkCommand.CommandText = "SELECT COUNT(*) FROM MenuItems WHERE Restaurant = @restaurant AND Name = @name AND Category = @category";
                    checkCommand.Parameters.AddWithValue("@restaurant", restaurant);
                    checkCommand.Parameters.AddWithValue("@name", name);
                    checkCommand.Parameters.AddWithValue("@category", category);

                    var count = Convert.ToInt32(checkCommand.ExecuteScalar());
                    if (count > 0)
                    {
                        Console.WriteLine($"Блюдо '{name}' уже существует в ресторане '{restaurant}', категория '{category}'");
                        return false;
                    }
                }

                // Добавляем блюдо
                using (var command = _connection.CreateCommand())
                {
                    command.CommandText = @"
                        INSERT INTO MenuItems (Restaurant, Name, Price, Category)
                        VALUES (@restaurant, @name, @price, @category)";

                    command.Parameters.AddWithValue("@restaurant", restaurant);
                    command.Parameters.AddWithValue("@name", name);
                    command.Parameters.AddWithValue("@price", price);
                    command.Parameters.AddWithValue("@category", category);

                    int rowsAffected = command.ExecuteNonQuery();

                    if (rowsAffected > 0)
                    {
                        Console.WriteLine($"✅ Блюдо '{name}' успешно добавлено в ресторан '{restaurant}'");
                        Console.WriteLine($"   Категория: {category}");
                        Console.WriteLine($"   Цена: {price:C}");
                        return true;
                    }
                    else
                    {
                        Console.WriteLine($"❌ Не удалось добавить блюдо '{name}'");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка при добавлении блюда '{name}': {ex.Message}");
                return false;
            }
        }

        // ============ СТАРЫЕ МЕТОДЫ (оставляем для совместимости) ============
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
                command.CommandText = "SELECT * FROM MenuItems WHERE Name != '[ПЕРВОЕ БЛЮДО - добавьте через меню]' ORDER BY Restaurant, Category, Name";

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

                        if (menuItem.Name != "[ПЕРВОЕ БЛЮДО - добавьте через меню]")
                        {
                            menuItems.Add(menuItem);
                        }
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

        // ============ УДАЛИТЬ ВСЕ ДУБЛИРОВАННЫЕ БЛЮДА ============
        public void RemoveDuplicateMenuItems()
        {
            try
            {
                EnsureConnectionOpen();

                using (var command = _connection.CreateCommand())
                {
                    // Удаляем дубликаты (оставляем первый)
                    command.CommandText = @"
                        DELETE FROM MenuItems 
                        WHERE Id NOT IN (
                            SELECT MIN(Id) 
                            FROM MenuItems 
                            GROUP BY Restaurant, Name, Category, Price
                        )";

                    int deleted = command.ExecuteNonQuery();
                    Console.WriteLine($"Удалено {deleted} дубликатов меню");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при удалении дубликатов: {ex.Message}");
            }
        }

        private void EnsureConnectionOpen()
        {
            if (_connection == null)
            {
                throw new InvalidOperationException("Соединение с базой данных не инициализировано");
            }

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
                        if (_connection.State != System.Data.ConnectionState.Closed)
                        {
                            _connection.Close();
                        }
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
}