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

        private static readonly Dictionary<long, string> _userRoles = new Dictionary<long, string>();

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
            if (msg.Text != null && !msg.Text.StartsWith("/start"))
            {
                if (!IsUserAuthorized(msg.From.Id))
                {
                    await _botClient.SendMessage(msg.Chat, "Пожалуйста, авторизуйтесь с помощью команды /start");
                    return;
                }
            }

            if (msg.Text == "/start")
            {
                var keyboard = new InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("Сотрудник", "employee")
                    },
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("Менеджер", "manager")
                    }
                });

                await _botClient.SendMessage(
                    msg.Chat,
                    "Авторизуйтесь",
                    replyMarkup: keyboard
                );
            }

            else if (msg.Text != null && IsUserAuthorized(msg.From.Id))
            {
                await HandleAuthorizedMessage(msg);
            }
        }

        private async Task OnUpdate(Update update)
        {
            if (update is { CallbackQuery: { } query })
            {
                if (query.Data == "employee")
                {
                    using (var db = new EmployeesDatabaseManager())
                    {
                        if (db.EmployeeExistsByTelegramId(query.From.Id))
                        {
                            _userRoles[query.From.Id] = "employee";
                            await _botClient.SendMessage(query.Message.Chat, "Успешно");
                            var currentEmployee = new Employee();
                            currentEmployee = db.GetEmployeeByTelegramId(query.From.Id);

                            await ShowEmployeeMainMenu(query, currentEmployee);
                        }
                        else
                        {
                            _userRoles[query.From.Id] = "manager";
                            await _botClient.SendMessage(query.Message.Chat, "Нет такого сотрудника");
                        }
                    }
                }
                if (query.Data == "manager")
                {
                    int managerId = int.Parse(File.ReadAllText("..\\..\\..\\Databases\\ManagerId.txt").Trim());
                    if (managerId == query.From.Id)
                    {
                        await _botClient.SendMessage(query.Message.Chat, "Успешно");
                    }
                    else
                    {
                        await _botClient.SendMessage(query.Message.Chat, "Нет такого менеджера");
                    }
                }
                await _botClient.AnswerCallbackQuery(query.Id);
            }
        }

        private bool IsUserAuthorized(long userId)
        {
            return _userRoles.ContainsKey(userId);
        }

        private string GetUserRole(long userId)
        {
            return _userRoles.ContainsKey(userId) ? _userRoles[userId] : null;
        }

        private async Task HandleAuthorizedMessage(Message msg)
        {
            var role = GetUserRole(msg.From.Id);

            if (role == "employee")
            {
                var currentEmployee = new Employee();
                using (var db = new EmployeesDatabaseManager()) 
                {
                    currentEmployee = db.GetEmployeeByTelegramId(msg.From.Id);
                }

                if (msg.Text == "/help")
                {
                    var helpText = "Команды /help, /logout";

                    await _botClient.SendMessage(msg.Chat, helpText);
                }
                else if (msg.Text == "/logout")
                {
                    _userRoles.Remove(msg.From.Id);
                    await _botClient.SendMessage(msg.Chat, "Вы вышли из системы. Используйте /start для повторной авторизации.");
                }
                else
                {
                    await ShowEmployeeMainMenu(msg, currentEmployee);
                }
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        private async Task ShowEmployeeMainMenu(Message msg, Employee currentEmployee)
        {
            var keyboard = new InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("Выбрать ресторан", "showRestoraunt")
                    },
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("Посмотреть заказ", "showOrder")
                    },
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("Подтвердить заказ", "confirmOrder")
                    }
                });

            await _botClient.SendMessage(
                msg.Chat,
                $"Вы авторизованы как сотрудник\n{currentEmployee.Name}   {currentEmployee.Amount}",
                replyMarkup: keyboard
            );
        }

        private async Task ShowEmployeeMainMenu(CallbackQuery query, Employee currentEmployee)
        {
            var keyboard = new InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("Выбрать ресторан", "showRestoraunt")
                    },
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("Посмотреть заказ", "showOrder")
                    },
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("Подтвердить заказ", "confirmOrder")
                    }
                });

            await _botClient.SendMessage(
                query.Message.Chat,
                $"Вы авторизованы как сотрудник\n{currentEmployee.Name}   {currentEmployee.Amount}",
                replyMarkup: keyboard
            );
        }
        public void StopBot()
        {
            _cts?.Cancel();
        }
    }
}