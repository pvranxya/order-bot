using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace order_bot
{
    public class OrderOrganizer
    {
        private readonly OrdersDatabaseManager _dbManager;

        public class PositionSummary
        {
            public string ItemName { get; set; }
            public decimal Price { get; set; }
            public int Count { get; set; }
            public decimal TotalAmount => Price * Count;

            public PositionSummary(string itemName, decimal price, int count)
            {
                ItemName = itemName;
                Price = price;
                Count = count;
            }

            public override string ToString()
            {
                return $"{ItemName}: {Count} × {Price:C} = {TotalAmount:C}";
            }
        }

        public class RestaurantStats
        {
            public string Name { get; set; }
            // Ключ: "Название|Цена" (на случай одинаковых названий с разной ценой)
            public Dictionary<string, PositionSummary> Positions { get; set; } = new();
            public int TotalOrders => Positions.Sum(p => p.Value.Count);
            public decimal TotalRevenue => Positions.Sum(p => p.Value.TotalAmount);

            public RestaurantStats(string name)
            {
                Name = name;
            }
        }

        public OrderOrganizer(OrdersDatabaseManager dbManager)
        {
            _dbManager = dbManager ?? throw new ArgumentNullException(nameof(dbManager));
        }

        public Dictionary<string, RestaurantStats> GetFullStatistics()
        {
            var allOrders = _dbManager.GetAllOrders();
            var result = new Dictionary<string, RestaurantStats>();

            foreach (var order in allOrders)
            {
                if (!result.ContainsKey(order.Restaurant))
                {
                    result[order.Restaurant] = new RestaurantStats(order.Restaurant);
                }

                var restaurant = result[order.Restaurant];

                // Используем Name из Order
                string itemName = !string.IsNullOrEmpty(order.Name) ? order.Name : $"Позиция {order.Price:C}";
                string positionKey = $"{itemName}|{order.Price}";

                if (!restaurant.Positions.ContainsKey(positionKey))
                {
                    restaurant.Positions[positionKey] = new PositionSummary(itemName, order.Price, 0);
                }

                restaurant.Positions[positionKey].Count += order.Count;
            }

            return result;
        }

        public RestaurantStats GetRestaurantStatistics(string restaurantName)
        {
            var orders = _dbManager.GetOrdersByRestaurant(restaurantName);
            var stats = new RestaurantStats(restaurantName);

            foreach (var order in orders)
            {
                string itemName = !string.IsNullOrEmpty(order.Name) ? order.Name : $"Позиция {order.Price:C}";
                string positionKey = $"{itemName}|{order.Price}";

                if (!stats.Positions.ContainsKey(positionKey))
                {
                    stats.Positions[positionKey] = new PositionSummary(itemName, order.Price, 0);
                }

                stats.Positions[positionKey].Count += order.Count;
            }

            return stats;
        }

        public string GetDetailedReport()
        {
            var stats = GetFullStatistics();
            var report = new StringBuilder();

            report.AppendLine("ОТЧЕТ ПО РЕСТОРАНАМ");
            report.AppendLine();

            foreach (var restaurant in stats.OrderBy(r => r.Key))
            {
                report.AppendLine($"🏢 {restaurant.Key}");
                report.AppendLine($"Позиция                Цена      Кол-во       Сумма");
                report.AppendLine(new string('-', 60));

                foreach (var position in restaurant.Value.Positions.Values.OrderBy(p => p.ItemName))
                {
                    report.AppendLine($"{position.ItemName,-20} {position.Price,8:C} {position.Count,8} {position.TotalAmount,12:C}");
                }

                report.AppendLine(new string('-', 60));
                report.AppendLine($"Итого по ресторану: {restaurant.Value.TotalRevenue,40:C}");
                report.AppendLine();
            }

            decimal grandTotal = stats.Sum(r => r.Value.TotalRevenue);
            report.AppendLine(new string('=', 60));
            report.AppendLine($"ОБЩАЯ СУММА ПО ВСЕМ РЕСТОРАНАМ: {grandTotal,25:C}");

            return report.ToString();
        }

        public Dictionary<string, decimal> GetRestaurantTotalRevenue()
        {
            var stats = GetFullStatistics();
            return stats.ToDictionary(
                r => r.Key,
                r => r.Value.TotalRevenue
            );
        }

        public List<KeyValuePair<string, decimal>> GetTopRestaurants(int topN = 5)
        {
            var revenue = GetRestaurantTotalRevenue();
            return revenue
                .OrderByDescending(r => r.Value)
                .Take(topN)
                .ToList();
        }

        public Dictionary<string, (string ItemName, int Count)> GetMostPopularPositionPerRestaurant()
        {
            var stats = GetFullStatistics();
            var result = new Dictionary<string, (string, int)>();

            foreach (var restaurant in stats)
            {
                if (restaurant.Value.Positions.Count > 0)
                {
                    var mostPopular = restaurant.Value.Positions
                        .OrderByDescending(p => p.Value.Count)
                        .First();

                    result[restaurant.Key] = (mostPopular.Value.ItemName, mostPopular.Value.Count);
                }
            }

            return result;
        }
    }
}
