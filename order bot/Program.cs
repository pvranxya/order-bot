using order_bot;

class Program
{
    static async Task Main(string[] args)
    {
        var bot = new TelegramBot();
        await bot.RunBot();
    }
}