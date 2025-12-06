using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace order_bot
{
    public class EmployeesDatabaseManager : IDisposable
    {
        private SqliteConnection _connection;
        private readonly string _connectionString;

        public EmployeesDatabaseManager(string databasePath = "..\\..\\..\\Databases\\employees.db")
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
                CREATE TABLE IF NOT EXISTS Employees (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL,
                    telegram_id INTEGER UNIQUE,
                    amount REAL DEFAULT 0.0,
                    office TEXT
                )";

                createTableCommand.ExecuteNonQuery();
            }
        }

        public void AddEmployee(Employee employee)
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = @"
                INSERT INTO Employees (name, telegram_id, amount, office)
                VALUES (@name, @telegramId, @amount, @office)";

                command.Parameters.AddWithValue("@name", employee.Name);
                command.Parameters.AddWithValue("@telegramId", employee.TelegramId);
                command.Parameters.AddWithValue("@amount", employee.Amount);
                command.Parameters.AddWithValue("@office", employee.Office ?? (object)DBNull.Value);

                command.ExecuteNonQuery();
            }
        }

        public int DeleteEmployeeByName(string name)
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM Employees WHERE name = @name";
                command.Parameters.AddWithValue("@name", name);

                return command.ExecuteNonQuery();
            }
        }

        public List<Employee> GetAllEmployees()
        {
            var employees = new List<Employee>();

            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = "SELECT * FROM Employees";

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        employees.Add(new Employee
                        {
                            Id = reader.GetInt32(0),
                            Name = reader.GetString(1),
                            TelegramId = reader.GetInt64(2),
                            Amount = reader.GetDecimal(3),
                            Office = reader.IsDBNull(4) ? null : reader.GetString(4)
                        });
                    }
                }
            }

            return employees;
        }

        public Employee GetEmployeeByTelegramId(long telegramId)
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = "SELECT * FROM Employees WHERE telegram_id = @telegramId";
                command.Parameters.AddWithValue("@telegramId", telegramId);

                using (var reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        return new Employee
                        {
                            Id = reader.GetInt32(0),
                            Name = reader.GetString(1),
                            TelegramId = reader.GetInt64(2),
                            Amount = reader.GetDecimal(3),
                            Office = reader.IsDBNull(4) ? null : reader.GetString(4)
                        };
                    }
                }
            }

            return null;
        }

        public void UpdateEmployee(Employee employee)
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = @"
                UPDATE Employees 
                SET name = @name, 
                    telegram_id = @telegramId, 
                    amount = @amount, 
                    office = @office 
                WHERE id = @id";

                command.Parameters.AddWithValue("@id", employee.Id);
                command.Parameters.AddWithValue("@name", employee.Name);
                command.Parameters.AddWithValue("@telegramId", employee.TelegramId);
                command.Parameters.AddWithValue("@amount", employee.Amount);
                command.Parameters.AddWithValue("@office", employee.Office ?? (object)DBNull.Value);

                command.ExecuteNonQuery();
            }
        }
        public int ClearAllEmployees()
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM Employees";

                return command.ExecuteNonQuery();
            }
        } 

        public bool EmployeeExistsByTelegramId(long telegramId)
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(1) FROM Employees WHERE telegram_id = @telegramId";
                command.Parameters.AddWithValue("@telegramId", telegramId);

                var result = command.ExecuteScalar();
                return Convert.ToInt32(result) > 0;
            }
        }
        public void Dispose()
        {
            _connection?.Dispose();
        }
    }
}
