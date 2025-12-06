using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace order_bot
{
    public class TelegramBot
    {
        private TelegramBotClient _botClient;
        private CancellationTokenSource _cts;

        public async Task RunBot()
        {
            _cts = new CancellationTokenSource();
            _botClient = new TelegramBotClient("7553461301:AAHdi2tbhlu0GrvWDLFs2HOE1Fpw46ZMQJw", cancellationToken: _cts.Token);

            var me = await _botClient.GetMe();

            _botClient.OnError += OnError;
            _botClient.OnMessage += OnMessage;
            _botClient.OnUpdate += OnUpdate;

            Console.WriteLine($"@{me.Username} is running... Press Enter to terminate");
            Console.ReadLine();

            _cts.Cancel();
        }

        private async Task OnError(Exception exception, HandleErrorSource source)
        {
            Console.WriteLine(exception);
        }

        private async Task OnMessage(Message msg, UpdateType type)
        {
            if (msg.Text == "/start")
            {
                var keyboard = new InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("Сотрудник", "employee"),
                        InlineKeyboardButton.WithCallbackData("Менеджер", "manager")
                    }
                });

                await _botClient.SendMessage(
                    msg.Chat,
                    "Авторизуйтесь",
                    replyMarkup: keyboard
                );
            }
        }

        private async Task OnUpdate(Update update)
        {
            if (update is { CallbackQuery: { } query })
            {
                if (query.Data == "employee")
                {

                }
                if (query.Data == "manager")
                {

                }
                await _botClient.AnswerCallbackQuery(query.Id, $"You picked {query.Data}");
                await _botClient.SendMessage(query.Message!.Chat, $"User {query.From} clicked on {query.Data}");
            }
        }

        public void StopBot()
        {
            _cts?.Cancel();
        }
    }
}