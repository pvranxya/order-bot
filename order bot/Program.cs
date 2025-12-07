using order_bot;

class Program
{
    static async Task Main(string[] args)
    {
        using (var db = new EmployeesDatabaseManager())
        {
            db.ClearAllEmployees();
        }
        using (var db = new OrdersDatabaseManager())
        {
            db.ClearAllOrders();
        }
        var bot = new TelegramBot();
        await bot.RunBot();
    }
}