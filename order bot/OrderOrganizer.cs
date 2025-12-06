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
            public decimal Price { get; set; }
            public int Count { get; set; }
            public decimal TotalAmount => Price * Count;

            public PositionSummary(decimal price, int count)
            {
                Price = price;
                Count = count;
            }

            public override string ToString()
            {
                return $"{Count} × {Price:C} = {TotalAmount:C}";
            }
        }

        public class RestaurantStats
        {
            public string Name { get; set; }
            public Dictionary<decimal, PositionSummary> Positions { get; set; } = new();
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

                if (!restaurant.Positions.ContainsKey(order.Price))
                {
                    restaurant.Positions[order.Price] = new PositionSummary(order.Price, 0);
                }

                restaurant.Positions[order.Price].Count += order.Count;
            }

            return result;
        }

        public RestaurantStats GetRestaurantStatistics(string restaurantName)
        {
            var orders = _dbManager.GetOrdersByRestaurant(restaurantName);
            var stats = new RestaurantStats(restaurantName);

            foreach (var order in orders)
            {
                if (!stats.Positions.ContainsKey(order.Price))
                {
                    stats.Positions[order.Price] = new PositionSummary(order.Price, 0);
                }

                stats.Positions[order.Price].Count += order.Count;
            }

            return stats;
        }

        public Dictionary<string, decimal> GetRestaurantTotalRevenue()
        {
            var stats = GetFullStatistics();
            return stats.ToDictionary(
                r => r.Key,
                r => r.Value.TotalRevenue
            );
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
                report.AppendLine($"Цена       Кол-во       Сумма");
                report.AppendLine(new string('-', 35));

                foreach (var position in restaurant.Value.Positions.OrderBy(p => p.Key))
                {
                    report.AppendLine($"{position.Key,10:C} {position.Value.Count,8} {position.Value.TotalAmount,12:C}");
                }

                report.AppendLine(new string('-', 35));
                report.AppendLine($"Итого: {restaurant.Value.TotalRevenue,25:C}");
                report.AppendLine();
            }

            decimal grandTotal = stats.Sum(r => r.Value.TotalRevenue);
            report.AppendLine(new string('=', 45));
            report.AppendLine($"ОБЩАЯ СУММА: {grandTotal,32:C}");

            return report.ToString();
        }

        public List<KeyValuePair<string, decimal>> GetTopRestaurants(int topN = 5)
        {
            var revenue = GetRestaurantTotalRevenue();
            return revenue
                .OrderByDescending(r => r.Value)
                .Take(topN)
                .ToList();
        }

        public Dictionary<string, (decimal Price, int Count)> GetMostPopularPositionPerRestaurant()
        {
            var stats = GetFullStatistics();
            var result = new Dictionary<string, (decimal, int)>();

            foreach (var restaurant in stats)
            {
                if (restaurant.Value.Positions.Count > 0)
                {
                    var mostPopular = restaurant.Value.Positions
                        .OrderByDescending(p => p.Value.Count)
                        .First();

                    result[restaurant.Key] = (mostPopular.Key, mostPopular.Value.Count);
                }
            }

            return result;
        }

        public Dictionary<decimal, int> GetGlobalPriceStatistics()
        {
            var allOrders = _dbManager.GetAllOrders();
            var result = new Dictionary<decimal, int>();

            foreach (var order in allOrders)
            {
                if (!result.ContainsKey(order.Price))
                {
                    result[order.Price] = 0;
                }
                result[order.Price] += order.Count;
            }

            return result;
        }
    }
}
