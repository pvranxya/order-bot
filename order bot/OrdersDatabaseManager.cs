using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace order_bot
{
    public class OrdersDatabaseManager : IDisposable
    {
        private readonly string _connectionString;

        public OrdersDatabaseManager(string databasePath = "..\\..\\..\\Databases\\orders.db")
        {
            _connectionString = $"Data Source={databasePath}";
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();

                var createTableCommand = connection.CreateCommand();
                createTableCommand.CommandText = @"
                CREATE TABLE IF NOT EXISTS Orders (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Restaurant TEXT NOT NULL,
                    Name TEXT NOT NULL,
                    Price DECIMAL(10,2) NOT NULL,
                    Count INTEGER NOT NULL DEFAULT 1,
                    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
                )";

                createTableCommand.ExecuteNonQuery();
            }
        }

        // Добавить заказ (полная версия)
        public void AddOrder(Order order)
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = @"
                INSERT INTO Orders (Restaurant, Name, Price, Count)
                VALUES (@restaurant, @name, @price, @count)";

                command.Parameters.AddWithValue("@restaurant", order.Restaurant);
                command.Parameters.AddWithValue("@name", order.Name);
                command.Parameters.AddWithValue("@price", order.Price);
                command.Parameters.AddWithValue("@count", order.Count);

                command.ExecuteNonQuery();

                // Получаем Id только что добавленного заказа
                command.CommandText = "SELECT last_insert_rowid()";
                order.Id = Convert.ToInt32(command.ExecuteScalar());
            }
        }

        // Добавить заказ (упрощенная версия)
        public void AddOrder(string restaurant, string name, decimal price, int count = 1)
        {
            var order = new Order
            {
                Restaurant = restaurant,
                Name = name,
                Price = price,
                Count = count
            };

            AddOrder(order);
        }

        // Очистить все заказы
        public int ClearAllOrders()
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM Orders";

                int deletedCount = command.ExecuteNonQuery();

                // Сбрасываем автоинкремент
                command.CommandText = "DELETE FROM sqlite_sequence WHERE name='Orders'";
                command.ExecuteNonQuery();

                return deletedCount;
            }
        }

        // Вернуть список заказов с определенного ресторана
        public List<Order> GetOrdersByRestaurant(string restaurant)
        {
            var orders = new List<Order>();

            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = "SELECT * FROM Orders WHERE Restaurant = @restaurant ORDER BY Id DESC";
                command.Parameters.AddWithValue("@restaurant", restaurant);

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        orders.Add(new Order
                        {
                            Id = reader.GetInt32(0),
                            Restaurant = reader.GetString(1),
                            Name = reader.GetString(2),
                            Price = reader.GetDecimal(3),
                            Count = reader.GetInt32(4)
                        });
                    }
                }
            }

            return orders;
        }

        // Получить все заказы
        public List<Order> GetAllOrders()
        {
            var orders = new List<Order>();

            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = "SELECT * FROM Orders ORDER BY Id DESC";

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        orders.Add(new Order
                        {
                            Id = reader.GetInt32(0),
                            Restaurant = reader.GetString(1),
                            Name = reader.GetString(2),
                            Price = reader.GetDecimal(3),
                            Count = reader.GetInt32(4)
                        });
                    }
                }
            }

            return orders;
        }

        // Получить количество заказов по ресторану
        public int GetOrderCountByRestaurant(string restaurant)
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM Orders WHERE Restaurant = @restaurant";
                command.Parameters.AddWithValue("@restaurant", restaurant);

                return Convert.ToInt32(command.ExecuteScalar());
            }
        }

        // Проверить существование заказа по ID
        public bool OrderExists(int id)
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(1) FROM Orders WHERE Id = @id";
                command.Parameters.AddWithValue("@id", id);

                var result = command.ExecuteScalar();
                return Convert.ToInt32(result) > 0;
            }
        }

        // Получить заказ по ID
        public Order GetOrderById(int id)
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = "SELECT * FROM Orders WHERE Id = @id";
                command.Parameters.AddWithValue("@id", id);

                using (var reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        return new Order
                        {
                            Id = reader.GetInt32(0),
                            Restaurant = reader.GetString(1),
                            Name = reader.GetString(2),
                            Price = reader.GetDecimal(3),
                            Count = reader.GetInt32(4)
                        };
                    }
                }
            }

            return null;
        }

        // Обновить заказ
        public bool UpdateOrder(Order order)
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = @"
                UPDATE Orders 
                SET Restaurant = @restaurant, 
                    Name = @name, 
                    Price = @price, 
                    Count = @count 
                WHERE Id = @id";

                command.Parameters.AddWithValue("@restaurant", order.Restaurant);
                command.Parameters.AddWithValue("@name", order.Name);
                command.Parameters.AddWithValue("@price", order.Price);
                command.Parameters.AddWithValue("@count", order.Count);
                command.Parameters.AddWithValue("@id", order.Id);

                int rowsAffected = command.ExecuteNonQuery();
                return rowsAffected > 0;
            }
        }

        // Удалить заказ по ID
        public bool DeleteOrder(int id)
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM Orders WHERE Id = @id";
                command.Parameters.AddWithValue("@id", id);

                int rowsAffected = command.ExecuteNonQuery();
                return rowsAffected > 0;
            }
        }

        // Поиск заказов по названию позиции
        public List<Order> GetOrdersByItemName(string itemName)
        {
            var orders = new List<Order>();

            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = "SELECT * FROM Orders WHERE Name LIKE @name ORDER BY Id DESC";
                command.Parameters.AddWithValue("@name", $"%{itemName}%");

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        orders.Add(new Order
                        {
                            Id = reader.GetInt32(0),
                            Restaurant = reader.GetString(1),
                            Name = reader.GetString(2),
                            Price = reader.GetDecimal(3),
                            Count = reader.GetInt32(4)
                        });
                    }
                }
            }

            return orders;
        }

        // Получить общую сумму заказов по ресторану
        public decimal GetTotalRevenueByRestaurant(string restaurant)
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = "SELECT SUM(Price * Count) FROM Orders WHERE Restaurant = @restaurant";
                command.Parameters.AddWithValue("@restaurant", restaurant);

                var result = command.ExecuteScalar();
                return result != DBNull.Value ? Convert.ToDecimal(result) : 0;
            }
        }

        // Получить общую сумму всех заказов
        public decimal GetTotalRevenue()
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = "SELECT SUM(Price * Count) FROM Orders";

                var result = command.ExecuteScalar();
                return result != DBNull.Value ? Convert.ToDecimal(result) : 0;
            }
        }

        public void Dispose()
        {
            // Освобождение ресурсов если нужно
        }
    }
}
