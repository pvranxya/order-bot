using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using System.Timers;

namespace order_bot
{
    public class TelegramBot
    {
        private TelegramBotClient _botClient;
        private CancellationTokenSource _cts;
        private static System.Timers.Timer _deadlineTimer;
        private static System.Timers.Timer _midnightTimer;
        private static DateTime _deadlineTime = DateTime.MinValue;
        private static bool _isDeadlinePassed = false;

        private static readonly Dictionary<long, string> _userRoles = new Dictionary<long, string>();
        private static readonly Dictionary<long, UserState> _userStates = new Dictionary<long, UserState>();

        // Класс для хранения состояния пользователя
        private class UserState
        {
            public string StateType { get; set; } = "main"; // main, restaurant_selection, category_selection, item_selection, adding_employee_telegram, adding_employee_name, adding_employee_amount, adding_restaurant_name, adding_menu_item, adding_menu_item_details, setting_deadline, topup_employee_id, topup_employee_amount, topup_all_amount
            public string SelectedRestaurant { get; set; }
            public string SelectedCategory { get; set; }
            public List<OrderItem> SelectedItems { get; set; } = new List<OrderItem>();

            // Словари для сопоставления чисел с реальными значениями
            public Dictionary<int, string> RestaurantMapping { get; set; }
            public Dictionary<int, string> CategoryMapping { get; set; }
            public Dictionary<int, MenuItem> ItemMapping { get; set; }

            // Для добавления сотрудников и ресторанов
            public string TempData { get; set; } // Для временного хранения данных
            public Employee TempEmployee { get; set; } // Для временного хранения данных сотрудника
        }

        // Класс для хранения выбранного элемента заказа
        private class OrderItem
        {
            public MenuItem MenuItem { get; set; }
            public int Quantity { get; set; }
            public decimal TotalPrice => MenuItem?.Price * Quantity ?? 0;
        }

        public async Task RunBot()
        {
            _cts = new CancellationTokenSource();
            _botClient = new TelegramBotClient("8388656777:AAFR0BynVg-tcJuSzYgBXUMOgVH1qpVEcsI", cancellationToken: _cts.Token);

            var me = await _botClient.GetMe();

            // Устанавливаем команды меню
            await SetBotCommands();

            _botClient.OnError += OnError;
            _botClient.OnMessage += OnMessage;
            _botClient.OnUpdate += OnUpdate;

            // Запускаем таймер для проверки дедлайна каждую минуту
            StartDeadlineTimer();

            // Запускаем таймер для очистки заказов в полночь
            StartMidnightTimer();

            Console.WriteLine($"@{me.Username} is running... Press Enter to terminate");
            Console.ReadLine();

            _cts.Cancel();
            _deadlineTimer?.Stop();
            _midnightTimer?.Stop();
        }

        private async Task SetBotCommands()
        {
            try
            {
                // Общие команды для всех пользователей
                var commands = new List<BotCommand>
                {
                    new BotCommand
                    {
                        Command = "start",
                        Description = "🚀 Запустить бота / авторизация"
                    },
                    new BotCommand
                    {
                        Command = "help",
                        Description = "📋 Показать справку"
                    },
                    new BotCommand
                    {
                        Command = "logout",
                        Description = "🚪 Выйти из системы"
                    }
                };

                await _botClient.SetMyCommands(
                    commands: commands,
                    scope: BotCommandScope.Default(),
                    cancellationToken: _cts.Token
                );

                Console.WriteLine("Команды бота установлены");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при установке команд: {ex.Message}");
            }
        }

        private async Task UpdateCommandsForUser(long userId)
        {
            try
            {
                if (IsUserAuthorized(userId))
                {
                    var role = GetUserRole(userId);
                    var commands = new List<BotCommand>
                    {
                        new BotCommand
                        {
                            Command = "start",
                            Description = "🚀 Запустить бота"
                        },
                        new BotCommand
                        {
                            Command = "help",
                            Description = "📋 Справка"
                        },
                        new BotCommand
                        {
                            Command = "logout",
                            Description = "🚪 Выйти"
                        }
                    };

                    if (role == "employee")
                    {
                        commands.Add(new BotCommand
                        {
                            Command = "balance",
                            Description = "💰 Мой баланс"
                        });
                    }

                    await _botClient.SetMyCommands(
                        commands: commands,
                        scope: new BotCommandScopeChat { ChatId = userId },
                        languageCode: "ru",
                        cancellationToken: _cts.Token
                    );
                }
                else
                {
                    // Сброс к стандартным командам
                    await _botClient.DeleteMyCommands(
                        scope: new BotCommandScopeChat { ChatId = userId },
                        cancellationToken: _cts.Token
                    );
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при обновлении команд для пользователя {userId}: {ex.Message}");
            }
        }

        private void StartDeadlineTimer()
        {
            _deadlineTimer = new System.Timers.Timer(60000); // Проверка каждую минуту
            _deadlineTimer.Elapsed += CheckDeadline;
            _deadlineTimer.AutoReset = true;
            _deadlineTimer.Enabled = true;
        }

        private void StartMidnightTimer()
        {
            // Вычисляем время до следующей полночи
            var now = DateTime.Now;
            var midnight = now.Date.AddDays(1);
            var timeToMidnight = midnight - now;

            _midnightTimer = new System.Timers.Timer(timeToMidnight.TotalMilliseconds);
            _midnightTimer.Elapsed += async (s, e) =>
            {
                await ClearOrdersAtMidnight();

                // Перезапускаем таймер на следующий день
                _midnightTimer.Interval = TimeSpan.FromDays(1).TotalMilliseconds;
                _midnightTimer.Start();
            };
            _midnightTimer.AutoReset = false;
            _midnightTimer.Enabled = true;

            Console.WriteLine($"Очистка заказов запланирована на {midnight:HH:mm:ss}");
        }

        private async Task ClearOrdersAtMidnight()
        {
            try
            {
                using (var ordersDb = new OrdersDatabaseManager())
                {
                    int deletedCount = ordersDb.ClearAllOrders();
                    Console.WriteLine($"Полночь. Очищено {deletedCount} заказов.");

                    // Сбрасываем флаг дедлайна для нового дня
                    _isDeadlinePassed = false;

                    // Уведомляем менеджера
                    int managerId = int.Parse(File.ReadAllText("..\\..\\..\\Databases\\ManagerId.txt").Trim());
                    await _botClient.SendMessage(
                        chatId: managerId,
                        text: $"⏰ Полночь. Очищено {deletedCount} заказов. Начинается новый день."
                    );
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при очистке заказов в полночь: {ex.Message}");
            }
        }

        private async void CheckDeadline(object sender, ElapsedEventArgs e)
        {
            if (_deadlineTime != DateTime.MinValue && !_isDeadlinePassed)
            {
                var now = DateTime.Now;
                if (now >= _deadlineTime)
                {
                    _isDeadlinePassed = true;
                    await SendDailyReport();
                }
                else
                {
                    // Проверяем, не прошел ли дедлайн сегодня
                    var todayDeadline = new DateTime(now.Year, now.Month, now.Day,
                        _deadlineTime.Hour, _deadlineTime.Minute, 0);

                    if (now >= todayDeadline)
                    {
                        _isDeadlinePassed = true;
                        await SendDailyReport();
                    }
                }
            }
        }

        private async Task SendDailyReport()
        {
            try
            {
                // Генерируем и отправляем отчет
                using (var ordersDb = new OrdersDatabaseManager())
                {
                    var organizer = new OrderOrganizer(ordersDb);
                    var reportManager = new ReportManager(organizer);

                    // Сохраняем отчет в файл
                    string reportPath = $"report_{DateTime.Now:yyyyMMdd_HHmmss}.html";
                    reportManager.SaveHtmlReport(reportPath, $"Ежедневный отчет {DateTime.Now:dd.MM.yyyy}");

                    // Отправляем отчет менеджеру
                    await SendReportToManager(reportPath);

                    // Уведомляем всех сотрудников о прохождении дедлайна
                    await NotifyEmployeesAboutDeadline();

                    Console.WriteLine($"Дедлайн прошел. Отчет отправлен менеджеру.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при обработке дедлайна: {ex.Message}");
            }
        }

        private async Task NotifyEmployeesAboutDeadline()
        {
            try
            {
                using (var db = new EmployeesDatabaseManager())
                {
                    var employees = db.GetAllEmployees();

                    foreach (var employee in employees)
                    {
                        if (employee.TelegramId > 0)
                        {
                            try
                            {
                                await _botClient.SendMessage(
                                    chatId: employee.TelegramId,
                                    text: $"⏰ Дедлайн на сегодня ({DateTime.Now:dd.MM.yyyy}) пройден!\n" +
                                          $"Новые заказы не принимаются. Заказы будут очищены в полночь."
                                );
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Не удалось уведомить сотрудника {employee.Name}: {ex.Message}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при уведомлении сотрудников: {ex.Message}");
            }
        }

        private async Task SendReportToManager(string reportPath)
        {
            try
            {
                // Находим ID менеджера из файла
                int managerId = int.Parse(File.ReadAllText("..\\..\\..\\Databases\\ManagerId.txt").Trim());

                // Отправляем сообщение менеджеру
                await using (var stream = System.IO.File.OpenRead(reportPath))
                {
                    var inputFile = InputFile.FromStream(stream, Path.GetFileName(reportPath));

                    await _botClient.SendDocument(
                        chatId: managerId,
                        document: inputFile,
                        caption: $"📊 Ежедневный отчет за {DateTime.Now:dd.MM.yyyy}",
                        parseMode: ParseMode.Html
                    );
                }

                Console.WriteLine($"Отчет отправлен менеджеру {managerId}");

                // Удаляем файл отчета после отправки
                File.Delete(reportPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка отправки отчета: {ex.Message}");
            }
        }

        private async Task OnError(Exception exception, HandleErrorSource source)
        {
            Console.WriteLine(exception);
        }

        private async Task OnMessage(Message msg, UpdateType type)
        {
            // Обработка быстрых команд в любом состоянии
            if (msg.Text != null)
            {
                if (msg.Text == "/start")
                {
                    await HandleStartCommand(msg);
                    return;
                }

                if (msg.Text == "/logout")
                {
                    await HandleLogoutCommand(msg);
                    return;
                }

                if (msg.Text == "/help")
                {
                    await HandleHelpCommand(msg);
                    return;
                }

                if (msg.Text == "/balance" && IsUserAuthorized(msg.From.Id) && GetUserRole(msg.From.Id) == "employee")
                {
                    await HandleBalanceCommand(msg);
                    return;
                }
            }

            // Остальная логика обработки сообщений
            if (msg.Text != null && !msg.Text.StartsWith("/"))
            {
                if (!IsUserAuthorized(msg.From.Id))
                {
                    await _botClient.SendMessage(msg.Chat,
                        "Пожалуйста, авторизуйтесь с помощью команды /start\n\n" +
                        "Используйте команды из меню слева для навигации.");
                    return;
                }
            }

            if (msg.Text != null && IsUserAuthorized(msg.From.Id))
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
                    await HandleEmployeeAuth(query);
                }
                if (query.Data == "manager")
                {
                    await HandleManagerAuth(query);
                }

                // Обработка callback-кнопок основного меню сотрудника
                if (query.Data == "showRestoraunt")
                {
                    await HandleRestaurantSelection(query);
                }
                if (query.Data == "showOrder")
                {
                    await ShowCurrentOrder(query);
                }
                if (query.Data == "confirmOrder")
                {
                    await ConfirmOrder(query);
                }
                if (query.Data == "backToMain")
                {
                    await ReturnToEmployeeMainMenu(query);
                }

                // Обработка callback-кнопок менеджера
                if (query.Data == "addEmployee")
                {
                    await StartAddEmployee(query);
                }
                if (query.Data == "addRestaurant")
                {
                    await StartAddRestaurant(query);
                }
                if (query.Data == "setDeadline")
                {
                    await StartSetDeadline(query);
                }
                if (query.Data == "requestReport")
                {
                    await RequestReport(query);
                }
                if (query.Data == "backToManagerMain")
                {
                    await ShowManagerMainMenu(query);
                }
                if (query.Data == "addMenuItem")
                {
                    await StartAddMenuItem(query);
                }
                if (query.Data == "clearOrder")
                {
                    await ClearOrder(query);
                }
                if (query.Data == "topupBalance")
                {
                    await StartTopupBalance(query);
                }
                if (query.Data == "topupAllEmployees")
                {
                    await StartTopupAllEmployees(query);
                }

                await _botClient.AnswerCallbackQuery(query.Id);
            }
        }

        // Обработка быстрых команд
        private async Task HandleStartCommand(Message msg)
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
                "🍽️ Добро пожаловать в систему заказов!\n\n" +
                "Выберите роль для авторизации:",
                replyMarkup: keyboard
            );
        }

        private async Task HandleLogoutCommand(Message msg)
        {
            if (IsUserAuthorized(msg.From.Id))
            {
                var role = GetUserRole(msg.From.Id);
                _userRoles.Remove(msg.From.Id);
                _userStates.Remove(msg.From.Id);

                // Обновляем команды меню для пользователя
                await UpdateCommandsForUser(msg.From.Id);

                await _botClient.SendMessage(msg.Chat,
                    $"✅ Вы успешно вышли из системы ({role}).\n" +
                    "Для повторной авторизации используйте команду /start");
            }
            else
            {
                await _botClient.SendMessage(msg.Chat,
                    "❌ Вы не авторизованы.\n" +
                    "Для авторизации используйте команду /start");
            }
        }

        private async Task HandleHelpCommand(Message msg)
        {
            var message = "📋 Доступные команды:\n\n";

            if (IsUserAuthorized(msg.From.Id))
            {
                var role = GetUserRole(msg.From.Id);

                message += "Основные команды (доступны в меню слева):\n";
                message += "/start - авторизация\n";
                message += "/logout - выход из системы\n";
                message += "/help - показать справку\n\n";

                if (role == "employee")
                {
                    message += "Команды для сотрудников:\n";
                    message += "/balance - показать баланс\n";
                    message += "\n⚡ Быстрые действия через кнопки меню:\n";
                    message += "• Выбрать ресторан 🍽️\n";
                    message += "• Посмотреть заказ 📋\n";
                    message += "• Подтвердить заказ ✅\n";
                }
                else if (role == "manager")
                {
                    message += "⚡ Управление через кнопки меню:\n";
                    message += "• Добавить сотрудника ➕\n";
                    message += "• Добавить ресторан/блюдо 🏪🍽️\n";
                    message += "• Установить дедлайн ⏰\n";
                    message += "• Пополнить баланс 💰\n";
                    message += "• Запросить отчет 📊\n";
                }
            }
            else
            {
                message += "Команды для новых пользователей:\n";
                message += "/start - авторизация в системе\n";
                message += "/help - показать эту справку\n\n";
                message += "Для доступа к полному функционалу необходимо авторизоваться.";
            }

            await _botClient.SendMessage(msg.Chat, message);
        }

        private async Task HandleBalanceCommand(Message msg)
        {
            using (var db = new EmployeesDatabaseManager())
            {
                var employee = db.GetEmployeeByTelegramId(msg.From.Id);
                if (employee != null)
                {
                    await _botClient.SendMessage(msg.Chat,
                        $"💰 Ваш баланс: {employee.Amount:C}\n" +
                        $"👤 Имя: {employee.Name}");
                }
                else
                {
                    await _botClient.SendMessage(msg.Chat, "❌ Сотрудник не найден.");
                }
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
                // Проверяем, не прошел ли дедлайн
                if (_isDeadlinePassed)
                {
                    await _botClient.SendMessage(msg.Chat, "❌ Дедлайн на сегодня уже прошел. Заказы не принимаются.");
                    return;
                }

                await HandleEmployeeMessage(msg);
            }
            else if (role == "manager")
            {
                // ПРОВЕРЯЕМ СОСТОЯНИЕ ДОБАВЛЕНИЯ БЛЮДА
                if (_userStates.TryGetValue(msg.From.Id, out var userState))
                {
                    if (userState.StateType == "adding_menu_item_details")
                    {
                        await HandleAddMenuItem(msg);
                        return;
                    }
                    else if (userState.StateType == "adding_menu_item")
                    {
                        await HandleAddMenuItemRestaurantSelection(msg);
                        return;
                    }
                }

                await HandleManagerMessage(msg);
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        #region Employee Methods
        private async Task HandleEmployeeMessage(Message msg)
        {
            if (!_userStates.TryGetValue(msg.From.Id, out var userState))
            {
                await ShowEmployeeMainMenu(msg, new Employee());
                return;
            }

            // Обработка состояний сотрудника
            if (userState.StateType == "restaurant_selection")
            {
                await HandleRestaurantNumberInput(msg);
                return;
            }
            else if (userState.StateType == "category_selection" && !string.IsNullOrEmpty(userState.SelectedRestaurant))
            {
                await HandleCategoryNumberInput(msg);
                return;
            }
            else if (userState.StateType == "item_selection" && !string.IsNullOrEmpty(userState.SelectedRestaurant) &&
                     !string.IsNullOrEmpty(userState.SelectedCategory))
            {
                await HandleItemNumberInput(msg);
                return;
            }

            // Если не в состоянии, показываем главное меню
            var currentEmployee = new Employee();
            using (var db = new EmployeesDatabaseManager())
            {
                currentEmployee = db.GetEmployeeByTelegramId(msg.From.Id);
            }

            await ShowEmployeeMainMenu(msg, currentEmployee);
        }

        private async Task HandleEmployeeAuth(CallbackQuery query)
        {
            using (var db = new EmployeesDatabaseManager())
            {
                if (db.EmployeeExistsByTelegramId(query.From.Id))
                {
                    _userRoles[query.From.Id] = "employee";
                    _userStates[query.From.Id] = new UserState();

                    var currentEmployee = db.GetEmployeeByTelegramId(query.From.Id);

                    // Обновляем команды меню для сотрудника
                    await UpdateCommandsForUser(query.From.Id);

                    await _botClient.SendMessage(query.Message.Chat,
                        $"✅ Успешно авторизованы как сотрудник\n" +
                        $"👤 Имя: {currentEmployee.Name}\n" +
                        $"💰 Баланс: {currentEmployee.Amount:C}\n\n" +
                        "Используйте команды из меню слева для быстрого доступа.");

                    await ShowEmployeeMainMenu(query, currentEmployee);
                }
                else
                {
                    await _botClient.SendMessage(query.Message.Chat,
                        "❌ Сотрудник не найден.\n" +
                        "Обратитесь к менеджеру для добавления в систему.");
                }
            }
        }

        private async Task ShowEmployeeMainMenu(Message msg, Employee currentEmployee)
        {
            var message = $"👤 {currentEmployee.Name}\n" +
                         $"💰 Баланс: {currentEmployee.Amount:C}";

            // Проверяем дедлайн
            if (_isDeadlinePassed)
            {
                message += "\n\n⚠️ ДЕДЛАЙН ПРОЙДЕН\nЗаказы не принимаются";
            }
            else
            {
                message += "\n\n⚡ Используйте команды в меню слева";
            }

            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🍽️ Выбрать ресторан", "showRestoraunt")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("📋 Посмотреть заказ", "showOrder")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("✅ Подтвердить заказ", "confirmOrder")
                }
            });

            await _botClient.SendMessage(msg.Chat, message, replyMarkup: keyboard);
        }

        private async Task ShowEmployeeMainMenu(CallbackQuery query, Employee currentEmployee)
        {
            var message = $"👤 {currentEmployee.Name}\n" +
                         $"💰 Баланс: {currentEmployee.Amount:C}";

            if (_isDeadlinePassed)
            {
                message += "\n\n⚠️ ДЕДЛАЙН ПРОЙДЕН\nЗаказы не принимаются";
            }
            else
            {
                message += "\n\n⚡ Используйте команды в меню слева";
            }

            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🍽️ Выбрать ресторан", "showRestoraunt")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("📋 Посмотреть заказ", "showOrder")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("✅ Подтвердить заказ", "confirmOrder")
                }
            });

            await _botClient.SendMessage(query.Message.Chat, message, replyMarkup: keyboard);
        }

        private async Task ReturnToEmployeeMainMenu(CallbackQuery query)
        {
            var currentEmployee = new Employee();
            using (var db = new EmployeesDatabaseManager())
            {
                currentEmployee = db.GetEmployeeByTelegramId(query.From.Id);
            }

            if (_userStates.ContainsKey(query.From.Id))
            {
                _userStates[query.From.Id] = new UserState();
            }

            await ShowEmployeeMainMenu(query, currentEmployee);
        }

        private async Task ClearOrder(CallbackQuery query)
        {
            if (_userStates.TryGetValue(query.From.Id, out var userState))
            {
                userState.SelectedItems.Clear();
                userState.SelectedRestaurant = null;
                userState.SelectedCategory = null;
                userState.RestaurantMapping = null;
                userState.CategoryMapping = null;
                userState.ItemMapping = null;
                userState.StateType = "main";
            }

            await _botClient.SendMessage(query.Message.Chat, "✅ Заказ очищен.");
            await ReturnToEmployeeMainMenu(query);
        }

        // Методы для работы с заказами
        private async Task HandleRestaurantSelection(CallbackQuery query)
        {
            if (_isDeadlinePassed)
            {
                await _botClient.SendMessage(query.Message.Chat, "❌ Дедлайн на сегодня уже прошел. Заказы не принимаются.");
                return;
            }

            List<string> restaurants;
            using (var db = new MenuDatabaseManager())
            {
                restaurants = db.GetRestaurants();
            }

            if (restaurants.Count == 0)
            {
                await _botClient.SendMessage(query.Message.Chat, "Нет доступных ресторанов.");
                return;
            }

            var restaurantMapping = new Dictionary<int, string>();
            var message = "Выберите ресторан (введите номер):\n";

            for (int i = 0; i < restaurants.Count; i++)
            {
                int number = i + 1;
                restaurantMapping[number] = restaurants[i];
                message += $"{number}. {restaurants[i]}\n";
            }

            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("Назад", "backToMain") }
            });

            await _botClient.SendMessage(query.Message.Chat, message, replyMarkup: keyboard);

            if (_userStates.ContainsKey(query.From.Id))
            {
                var userState = _userStates[query.From.Id];
                userState.StateType = "restaurant_selection";
                userState.SelectedRestaurant = null;
                userState.SelectedCategory = null;
                userState.RestaurantMapping = restaurantMapping;
                userState.CategoryMapping = null;
                userState.ItemMapping = null;
            }
        }

        private async Task HandleRestaurantNumberInput(Message msg)
        {
            if (_isDeadlinePassed)
            {
                await _botClient.SendMessage(msg.Chat, "❌ Дедлайн на сегодня уже прошел. Заказы не принимаются.");
                return;
            }

            if (!_userStates.TryGetValue(msg.From.Id, out var userState) || userState.RestaurantMapping == null)
            {
                await _botClient.SendMessage(msg.Chat, "Произошла ошибка. Попробуйте начать заново.");
                return;
            }

            if (int.TryParse(msg.Text, out int restaurantNumber) && restaurantNumber > 0)
            {
                if (userState.RestaurantMapping.TryGetValue(restaurantNumber, out string selectedRestaurant))
                {
                    userState.SelectedRestaurant = selectedRestaurant;
                    userState.StateType = "category_selection";

                    List<string> categories;
                    using (var db = new MenuDatabaseManager())
                    {
                        categories = db.GetCategoriesByRestaurant(selectedRestaurant);
                    }

                    if (categories.Count == 0)
                    {
                        await _botClient.SendMessage(msg.Chat, $"В ресторане '{selectedRestaurant}' нет категорий блюд.");
                        return;
                    }

                    var categoryMapping = new Dictionary<int, string>();
                    var message = $"Вы выбрали ресторан: {selectedRestaurant}\n\nВыберите категорию (введите номер):\n";

                    for (int i = 0; i < categories.Count; i++)
                    {
                        int number = i + 1;
                        categoryMapping[number] = categories[i];
                        message += $"{number}. {categories[i]}\n";
                    }

                    userState.CategoryMapping = categoryMapping;
                    userState.ItemMapping = null;

                    var keyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("Назад", "backToMain") }
                    });

                    await _botClient.SendMessage(msg.Chat, message, replyMarkup: keyboard);
                }
                else
                {
                    await _botClient.SendMessage(msg.Chat, "Неверный номер ресторана. Попробуйте снова.");
                }
            }
            else
            {
                await _botClient.SendMessage(msg.Chat, "Пожалуйста, введите корректный номер ресторана.");
            }
        }

        private async Task HandleCategoryNumberInput(Message msg)
        {
            if (_isDeadlinePassed)
            {
                await _botClient.SendMessage(msg.Chat, "❌ Дедлайн на сегодня уже прошел. Заказы не принимаются.");
                return;
            }

            if (!_userStates.TryGetValue(msg.From.Id, out var userState) ||
                string.IsNullOrEmpty(userState.SelectedRestaurant) ||
                userState.CategoryMapping == null)
            {
                await _botClient.SendMessage(msg.Chat, "Произошла ошибка. Попробуйте начать заново.");
                return;
            }

            if (int.TryParse(msg.Text, out int categoryNumber) && categoryNumber > 0)
            {
                if (userState.CategoryMapping.TryGetValue(categoryNumber, out string selectedCategory))
                {
                    userState.SelectedCategory = selectedCategory;
                    userState.StateType = "item_selection";

                    List<MenuItem> menuItems;
                    using (var db = new MenuDatabaseManager())
                    {
                        menuItems = db.GetMenuItemsByRestaurantAndCategory(userState.SelectedRestaurant, selectedCategory);
                    }

                    if (menuItems.Count == 0)
                    {
                        await _botClient.SendMessage(msg.Chat, $"В категории '{selectedCategory}' нет блюд.");
                        return;
                    }

                    var itemMapping = new Dictionary<int, MenuItem>();
                    var message = $"Вы выбрали категорию: {selectedCategory}\n\nВыберите блюда (вводите номер и количество через пробел, например: '1 2'):\nДля выбора нескольких блюд вводите каждое на новой строке\n\n";

                    for (int i = 0; i < menuItems.Count; i++)
                    {
                        int number = i + 1;
                        itemMapping[number] = menuItems[i];
                        message += $"{number}. {menuItems[i].Name} - {menuItems[i].Price} руб.\n";
                    }

                    userState.ItemMapping = itemMapping;

                    var keyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("Назад", "backToMain") }
                    });

                    await _botClient.SendMessage(msg.Chat, message, replyMarkup: keyboard);
                }
                else
                {
                    await _botClient.SendMessage(msg.Chat, "Неверный номер категории. Попробуйте снова.");
                }
            }
            else
            {
                await _botClient.SendMessage(msg.Chat, "Пожалуйста, введите корректный номер категории.");
            }
        }

        private async Task HandleItemNumberInput(Message msg)
        {
            if (_isDeadlinePassed)
            {
                await _botClient.SendMessage(msg.Chat, "❌ Дедлайн на сегодня уже прошел. Заказы не принимаются.");
                return;
            }

            if (!_userStates.TryGetValue(msg.From.Id, out var userState) ||
                string.IsNullOrEmpty(userState.SelectedRestaurant) ||
                string.IsNullOrEmpty(userState.SelectedCategory) ||
                userState.ItemMapping == null)
            {
                await _botClient.SendMessage(msg.Chat, "Произошла ошибка. Попробуйте начать заново.");
                return;
            }

            try
            {
                var lines = msg.Text.Split('\n')
                    .Select(line => line.Trim())
                    .Where(line => !string.IsNullOrEmpty(line))
                    .ToList();

                if (lines.Count == 0)
                {
                    await _botClient.SendMessage(msg.Chat, "Пожалуйста, введите данные в формате: 'номер количество'");
                    return;
                }

                var newSelectedItems = new List<OrderItem>();
                var invalidInputs = new List<string>();

                foreach (var line in lines)
                {
                    var parts = line.Split(' ')
                        .Select(p => p.Trim())
                        .Where(p => !string.IsNullOrEmpty(p))
                        .ToArray();

                    if (parts.Length != 2)
                    {
                        invalidInputs.Add($"Некорректный формат: '{line}'");
                        continue;
                    }

                    if (!int.TryParse(parts[0], out int itemNumber) || itemNumber <= 0)
                    {
                        invalidInputs.Add($"Некорректный номер: '{parts[0]}'");
                        continue;
                    }

                    if (!int.TryParse(parts[1], out int quantity) || quantity <= 0)
                    {
                        invalidInputs.Add($"Некорректное количество: '{parts[1]}'");
                        continue;
                    }

                    if (userState.ItemMapping.TryGetValue(itemNumber, out MenuItem menuItem))
                    {
                        newSelectedItems.Add(new OrderItem
                        {
                            MenuItem = menuItem,
                            Quantity = quantity
                        });
                    }
                    else
                    {
                        invalidInputs.Add($"Блюдо с номером {itemNumber} не найдено");
                    }
                }

                if (invalidInputs.Any())
                {
                    var errorMessage = "Обнаружены ошибки:\n" + string.Join("\n", invalidInputs);
                    await _botClient.SendMessage(msg.Chat, errorMessage);
                }

                if (newSelectedItems.Any())
                {
                    userState.SelectedItems.AddRange(newSelectedItems);

                    var message = "✅ Добавлено в заказ:\n\n";
                    decimal addedTotalPrice = 0;

                    foreach (var item in newSelectedItems)
                    {
                        message += $"• {item.MenuItem.Name} x{item.Quantity} = {item.TotalPrice} руб.\n";
                        addedTotalPrice += item.TotalPrice;
                    }

                    message += $"\n💰 Итого добавлено на сумму: {addedTotalPrice} руб.";

                    if (userState.SelectedItems.Any())
                    {
                        message += "\n\n📋 Весь текущий заказ:";
                        decimal currentTotal = 0;

                        var itemsByRestaurant = userState.SelectedItems
                            .GroupBy(item => item.MenuItem.Restaurant)
                            .ToList();

                        foreach (var restaurantGroup in itemsByRestaurant)
                        {
                            message += $"\n\n🏪 Ресторан: {restaurantGroup.Key}";

                            foreach (var item in restaurantGroup)
                            {
                                message += $"\n   • {item.MenuItem.Name} x{item.Quantity} = {item.TotalPrice} руб.";
                                currentTotal += item.TotalPrice;
                            }
                        }

                        message += $"\n\n📊 Общая сумма заказа: {currentTotal} руб.";
                        message += $"\n📦 Количество позиций: {userState.SelectedItems.Count}";
                    }

                    message += "\n\nВы можете продолжить выбор или перейти к оформлению заказа.";

                    var keyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[]
                        {
                            InlineKeyboardButton.WithCallbackData("Продолжить выбор", "showRestoraunt"),
                            InlineKeyboardButton.WithCallbackData("Посмотреть заказ", "showOrder")
                        },
                        new[]
                        {
                            InlineKeyboardButton.WithCallbackData("Подтвердить заказ", "confirmOrder")
                        },
                        new[]
                        {
                            InlineKeyboardButton.WithCallbackData("Назад в меню", "backToMain")
                        }
                    });

                    await _botClient.SendMessage(msg.Chat, message, replyMarkup: keyboard);

                    userState.StateType = "main";
                    userState.SelectedCategory = null;
                    userState.CategoryMapping = null;
                    userState.ItemMapping = null;
                }
                else if (!invalidInputs.Any())
                {
                    await _botClient.SendMessage(msg.Chat, "Не выбрано ни одного блюда.");
                }
            }
            catch (Exception ex)
            {
                await _botClient.SendMessage(msg.Chat, $"Ошибка: {ex.Message}\nПожалуйста, вводите данные в формате: 'номер количество' (например: '1 2')");
            }
        }

        private async Task ShowCurrentOrder(CallbackQuery query)
        {
            if (!_userStates.TryGetValue(query.From.Id, out var userState))
            {
                await _botClient.SendMessage(query.Message.Chat, "У вас нет активного заказа.");
                return;
            }

            if (!userState.SelectedItems.Any())
            {
                await _botClient.SendMessage(query.Message.Chat, "Ваш заказ пуст.");
                return;
            }

            var message = "📋 Ваш заказ:\n\n";
            decimal totalPrice = 0;

            var itemsByRestaurant = userState.SelectedItems
                .GroupBy(item => item.MenuItem.Restaurant)
                .ToList();

            foreach (var group in itemsByRestaurant)
            {
                message += $"🏪 Ресторан: {group.Key}\n";

                foreach (var item in group)
                {
                    message += $"   • {item.MenuItem.Name} x{item.Quantity} = {item.TotalPrice} руб.\n";
                    totalPrice += item.TotalPrice;
                }
                message += "\n";
            }

            // Получаем баланс сотрудника
            using (var db = new EmployeesDatabaseManager())
            {
                var employee = db.GetEmployeeByTelegramId(query.From.Id);
                if (employee != null)
                {
                    message += $"💰 Общая сумма заказа: {totalPrice} руб.\n";
                    message += $"💳 Ваш баланс: {employee.Amount:C}\n";

                    if (employee.Amount < totalPrice)
                    {
                        message += $"❌ Недостаточно средств!\n";
                        message += $"Не хватает: {totalPrice - employee.Amount:C}\n";
                    }
                    else
                    {
                        message += $"✅ Достаточно средств!\n";
                        message += $"Останется после оплата: {employee.Amount - totalPrice:C}\n";
                    }
                }
            }

            message += $"📊 Количество позиций: {userState.SelectedItems.Count}";

            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("Продолжить выбор", "showRestoraunt"),
                    InlineKeyboardButton.WithCallbackData("Подтвердить заказ", "confirmOrder")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("Очистить заказ", "clearOrder"),
                    InlineKeyboardButton.WithCallbackData("Назад в меню", "backToMain")
                }
            });

            await _botClient.SendMessage(query.Message.Chat, message, replyMarkup: keyboard);
        }

        private async Task ConfirmOrder(CallbackQuery query)
        {
            if (_isDeadlinePassed)
            {
                await _botClient.SendMessage(query.Message.Chat, "❌ Дедлайн на сегодня уже прошел. Заказы не принимаются.");
                return;
            }

            if (!_userStates.TryGetValue(query.From.Id, out var userState))
            {
                await _botClient.SendMessage(query.Message.Chat, "У вас нет активного заказа.");
                return;
            }

            if (!userState.SelectedItems.Any())
            {
                await _botClient.SendMessage(query.Message.Chat, "Ваш заказ пуст.");
                return;
            }

            try
            {
                // Рассчитываем общую сумму заказа
                decimal totalPrice = userState.SelectedItems.Sum(item => item.TotalPrice);

                // Проверяем баланс сотрудника
                Employee currentEmployee;
                using (var db = new EmployeesDatabaseManager())
                {
                    currentEmployee = db.GetEmployeeByTelegramId(query.From.Id);

                    if (currentEmployee == null)
                    {
                        await _botClient.SendMessage(query.Message.Chat, "❌ Ошибка: сотрудник не найден в базе данных.");
                        return;
                    }

                    if (currentEmployee.Amount < totalPrice)
                    {
                        await _botClient.SendMessage(query.Message.Chat,
                            $"❌ Недостаточно средств!\n" +
                            $"Сумма заказа: {totalPrice:C}\n" +
                            $"Ваш баланс: {currentEmployee.Amount:C}\n" +
                            $"Не хватает: {totalPrice - currentEmployee.Amount:C}\n\n" +
                            $"Пожалуйста, уменьшите количество позиций в заказе.");
                        return;
                    }
                }

                // Создаем заказы в базе данных
                int ordersCreated = 0;
                using (var ordersDb = new OrdersDatabaseManager())
                {
                    // Создаем отдельные заказы для каждой позиции
                    foreach (var orderItem in userState.SelectedItems)
                    {
                        var order = new Order
                        {
                            Restaurant = orderItem.MenuItem.Restaurant,
                            Name = orderItem.MenuItem.Name,
                            Price = orderItem.TotalPrice,
                            Count = orderItem.Quantity
                        };

                        ordersDb.AddOrder(order);
                        ordersCreated++;
                    }
                }

                // Списание денег со счета сотрудника
                using (var db = new EmployeesDatabaseManager())
                {
                    currentEmployee.Amount -= totalPrice;
                    db.UpdateEmployee(currentEmployee);
                }

                // Верификация: проверяем, что заказы действительно сохранились
                bool verificationPassed = await VerifyOrderCreation(userState.SelectedItems);

                var message = "✅ Заказ успешно создан!\n\n";
                message += $"📋 Количество позиций: {ordersCreated}\n";
                message += $"💰 Сумма заказа: {totalPrice:C}\n";
                message += $"💳 Списано с баланса: {totalPrice:C}\n";
                message += $"📊 Новый баланс: {currentEmployee.Amount:C}\n";

                if (verificationPassed)
                {
                    message += "✅ Заказ подтвержден в базе данных\n";
                }
                else
                {
                    message += "⚠️ Ошибка при проверке заказа в базе данных\n";
                }

                message += "\nЗаказ был разбит на отдельные позиции в базе данных.";

                // Очищаем состояние пользователя
                userState.SelectedItems.Clear();
                userState.SelectedRestaurant = null;
                userState.SelectedCategory = null;
                userState.RestaurantMapping = null;
                userState.CategoryMapping = null;
                userState.ItemMapping = null;
                userState.StateType = "main";

                var keyboard = new InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("Создать новый заказ", "showRestoraunt")
                    },
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("В главное меню", "backToMain")
                    }
                });

                await _botClient.SendMessage(query.Message.Chat, message, replyMarkup: keyboard);
            }
            catch (Exception ex)
            {
                await _botClient.SendMessage(query.Message.Chat, $"❌ Ошибка при создании заказа: {ex.Message}");
            }
        }

        private async Task<bool> VerifyOrderCreation(List<OrderItem> selectedItems)
        {
            try
            {
                using (var ordersDb = new OrdersDatabaseManager())
                {
                    var allOrders = ordersDb.GetAllOrders();

                    // Проверяем, что количество заказов увеличилось
                    // Более сложную проверку можно добавить при необходимости
                    return allOrders.Count >= selectedItems.Count;
                }
            }
            catch
            {
                return false;
            }
        }
        #endregion

        #region Manager Methods
        private async Task HandleManagerAuth(CallbackQuery query)
        {
            int managerId = int.Parse(File.ReadAllText("..\\..\\..\\Databases\\ManagerId.txt").Trim());
            if (managerId == query.From.Id)
            {
                _userRoles[query.From.Id] = "manager";
                _userStates[query.From.Id] = new UserState();

                // Обновляем команды меню для менеджера
                await UpdateCommandsForUser(query.From.Id);

                await _botClient.SendMessage(query.Message.Chat,
                    "✅ Успешно авторизованы как менеджер\n\n" +
                    "Используйте команды из меню слева для быстрого доступа.");
                await ShowManagerMainMenu(query);
            }
            else
            {
                await _botClient.SendMessage(query.Message.Chat, "❌ Нет такого менеджера");
            }
        }

        private async Task HandleManagerMessage(Message msg)
        {
            if (!_userStates.TryGetValue(msg.From.Id, out var userState))
            {
                await ShowManagerMainMenu(msg);
                return;
            }

            // Обработка состояний менеджера
            if (userState.StateType == "adding_employee_telegram")
            {
                await HandleAddEmployeeTelegramId(msg);
                return;
            }
            else if (userState.StateType == "adding_employee_name")
            {
                await HandleAddEmployeeName(msg);
                return;
            }
            else if (userState.StateType == "adding_employee_amount")
            {
                await HandleAddEmployeeAmount(msg);
                return;
            }
            else if (userState.StateType == "adding_restaurant_name")
            {
                await HandleAddRestaurantName(msg);
                return;
            }
            else if (userState.StateType == "adding_menu_item")
            {
                await HandleAddMenuItemRestaurantSelection(msg);
                return;
            }
            else if (userState.StateType == "adding_menu_item_details")
            {
                await HandleAddMenuItem(msg);
                return;
            }
            else if (userState.StateType == "setting_deadline")
            {
                await HandleSetDeadline(msg);
                return;
            }
            else if (userState.StateType == "topup_employee_id")
            {
                await HandleTopupEmployeeId(msg);
                return;
            }
            else if (userState.StateType == "topup_employee_amount")
            {
                await HandleTopupEmployeeAmount(msg);
                return;
            }
            else if (userState.StateType == "topup_all_amount")
            {
                await HandleTopupAllEmployeesAmount(msg);
                return;
            }

            // Если не в состоянии, показываем главное меню
            await ShowManagerMainMenu(msg);
        }

        private async Task ShowManagerMainMenu(CallbackQuery query)
        {
            var message = "👑 Вы авторизованы как менеджер\n\n";

            if (_deadlineTime != DateTime.MinValue)
            {
                message += $"⏰ Текущий дедлайн: {_deadlineTime:HH:mm}\n";
                message += _isDeadlinePassed ? "⚠️ ДЕДЛАЙН ПРОЙДЕН\n" : "✅ ДЕДЛАЙН АКТИВЕН\n";
            }
            else
            {
                message += "⏰ Дедлайн не установлен\n";
            }

            message += "\n⚡ Используйте команды в меню слева";

            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("➕ Добавить сотрудника", "addEmployee")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🍽️ Добавить блюдо", "addMenuItem")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("⏰ Установить дедлайн", "setDeadline")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("💰 Пополнить баланс", "topupBalance"),
                    InlineKeyboardButton.WithCallbackData("💰 Пополнить всем", "topupAllEmployees")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("📊 Запросить отчет", "requestReport")
                }
            });

            await _botClient.SendMessage(query.Message.Chat, message, replyMarkup: keyboard);
        }

        private async Task ShowManagerMainMenu(Message msg)
        {
            var message = "👑 Вы авторизованы как менеджер\n\n";

            if (_deadlineTime != DateTime.MinValue)
            {
                message += $"⏰ Текущий дедлайн: {_deadlineTime:HH:mm}\n";
                message += _isDeadlinePassed ? "⚠️ ДЕДЛАЙН ПРОЙДЕН\n" : "✅ ДЕДЛАЙН АКТИВЕН\n";
            }
            else
            {
                message += "⏰ Дедлайн не установлен\n";
            }

            message += "\n⚡ Используйте команды в меню слева";

            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("➕ Добавить сотрудника", "addEmployee")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🏪 Добавить ресторан", "addRestaurant"),
                    InlineKeyboardButton.WithCallbackData("🍽️ Добавить блюдо", "addMenuItem")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("⏰ Установить дедлайн", "setDeadline")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("💰 Пополнить баланс", "topupBalance"),
                    InlineKeyboardButton.WithCallbackData("💰 Пополнить всем", "topupAllEmployees")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("📊 Запросить отчет", "requestReport")
                }
            });

            await _botClient.SendMessage(msg.Chat, message, replyMarkup: keyboard);
        }

        // 1. Добавление сотрудников
        private async Task StartAddEmployee(CallbackQuery query)
        {
            if (!_userStates.TryGetValue(query.From.Id, out var userState))
            {
                _userStates[query.From.Id] = new UserState();
                userState = _userStates[query.From.Id];
            }

            userState.StateType = "adding_employee_telegram";
            userState.TempEmployee = new Employee(); // Создаем временный объект сотрудника
            userState.TempData = null;

            await _botClient.SendMessage(query.Message.Chat,
                "➕ Добавление нового сотрудника\n\n" +
                "Введите Telegram ID сотрудника (цифровой идентификатор):\n\n" +
                "ℹ️ Как получить Telegram ID?\n" +
                "• Попросите сотрудника запустить бота @userinfobot\n" +
                "• Или используйте ID из сообщений\n\n" +
                "Для отмены введите 'отмена'");
        }

        private async Task HandleAddEmployeeTelegramId(Message msg)
        {
            if (!_userStates.TryGetValue(msg.From.Id, out var userState))
            {
                await ShowManagerMainMenu(msg);
                return;
            }

            string input = msg.Text.Trim();

            // Проверка на отмену
            if (input.ToLower() == "отмена")
            {
                userState.StateType = "main";
                userState.TempEmployee = null;
                await ShowManagerMainMenu(msg);
                return;
            }

            if (long.TryParse(input, out long telegramId) && telegramId > 0)
            {
                // Проверяем, не существует ли уже сотрудник с таким Telegram ID
                using (var db = new EmployeesDatabaseManager())
                {
                    var existingEmployee = db.GetEmployeeByTelegramId(telegramId);
                    if (existingEmployee != null)
                    {
                        await _botClient.SendMessage(msg.Chat,
                            $"❌ Сотрудник с Telegram ID {telegramId} уже существует!\n" +
                            $"👤 Имя: {existingEmployee.Name}\n" +
                            $"💰 Баланс: {existingEmployee.Amount:C}");

                        userState.StateType = "main";
                        await ShowManagerMainMenu(msg);
                        return;
                    }
                }

                userState.TempEmployee.TelegramId = telegramId;
                userState.StateType = "adding_employee_name";

                await _botClient.SendMessage(msg.Chat,
                    $"🆔 Telegram ID: {telegramId}\n\n" +
                    "Теперь введите имя нового сотрудника:\n" +
                    "Для отмены введите 'отмена'");
            }
            else
            {
                await _botClient.SendMessage(msg.Chat,
                    "❌ Неверный формат Telegram ID.\n" +
                    "Введите положительное число (например: 123456789) или 'отмена' для отмены:");
            }
        }

        private async Task HandleAddEmployeeName(Message msg)
        {
            if (!_userStates.TryGetValue(msg.From.Id, out var userState) || userState.TempEmployee == null)
            {
                await ShowManagerMainMenu(msg);
                return;
            }

            string input = msg.Text.Trim();

            // Проверка на отмену
            if (input.ToLower() == "отмена")
            {
                userState.StateType = "main";
                userState.TempEmployee = null;
                await ShowManagerMainMenu(msg);
                return;
            }

            if (string.IsNullOrWhiteSpace(input))
            {
                await _botClient.SendMessage(msg.Chat, "❌ Имя не может быть пустым. Введите имя сотрудника или 'отмена':");
                return;
            }

            userState.TempEmployee.Name = input;
            userState.StateType = "adding_employee_amount";

            await _botClient.SendMessage(msg.Chat,
                $"🆔 Telegram ID: {userState.TempEmployee.TelegramId}\n" +
                $"👤 Имя: {userState.TempEmployee.Name}\n\n" +
                "Теперь введите начальный баланс (десятичное число):\n" +
                "Пример: 1000.50\n" +
                "Для отмены введите 'отмена'");
        }

        private async Task HandleAddEmployeeAmount(Message msg)
        {
            if (!_userStates.TryGetValue(msg.From.Id, out var userState) || userState.TempEmployee == null)
            {
                await ShowManagerMainMenu(msg);
                return;
            }

            string input = msg.Text.Trim();

            // Проверка на отмену
            if (input.ToLower() == "отмена")
            {
                userState.StateType = "main";
                userState.TempEmployee = null;
                await ShowManagerMainMenu(msg);
                return;
            }

            if (decimal.TryParse(input, out decimal amount) && amount >= 0)
            {
                userState.TempEmployee.Amount = amount;

                try
                {
                    // Сохраняем сотрудника в базу данных
                    using (var db = new EmployeesDatabaseManager())
                    {
                        db.AddEmployee(userState.TempEmployee);
                    }

                    var message = "✅ Сотрудник успешно добавлен!\n\n";
                    message += $"📋 Данные сотрудника:\n";
                    message += $"🆔 Telegram ID: {userState.TempEmployee.TelegramId}\n";
                    message += $"👤 Имя: {userState.TempEmployee.Name}\n";
                    message += $"💰 Начальный баланс: {amount:C}\n\n";
                    message += $"✅ Теперь сотрудник может авторизоваться в боте с помощью команды /start";

                    await _botClient.SendMessage(msg.Chat, message);

                    // Сбрасываем состояние и показываем главное меню
                    userState.StateType = "main";
                    userState.TempEmployee = null;
                    userState.TempData = null;

                    // Показываем кнопки для дальнейших действий
                    var successKeyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[]
                        {
                            InlineKeyboardButton.WithCallbackData("➕ Добавить еще сотрудника", "addEmployee"),
                            InlineKeyboardButton.WithCallbackData("💰 Пополнить баланс", "topupBalance")
                        },
                        new[]
                        {
                            InlineKeyboardButton.WithCallbackData("🏠 В главное меню", "backToManagerMain")
                        }
                    });

                    await _botClient.SendMessage(msg.Chat, "Выберите действие:", replyMarkup: successKeyboard);
                }
                catch (Exception ex)
                {
                    await _botClient.SendMessage(msg.Chat,
                        $"❌ Ошибка при добавлении сотрудника в базу данных:\n{ex.Message}");

                    userState.StateType = "main";
                    await ShowManagerMainMenu(msg);
                }
            }
            else
            {
                await _botClient.SendMessage(msg.Chat,
                    "❌ Неверный формат суммы. Пожалуйста, введите десятичное число (например: 1000.50) или 'отмена':");
            }
        }

        // 2. Добавление ресторанов
        private async Task StartAddRestaurant(CallbackQuery query)
        {
            if (!_userStates.TryGetValue(query.From.Id, out var userState))
            {
                _userStates[query.From.Id] = new UserState();
                userState = _userStates[query.From.Id];
            }

            userState.StateType = "adding_restaurant_name";
            userState.TempData = null;

            await _botClient.SendMessage(query.Message.Chat,
                "🏪 Добавление нового ресторана\n\n" +
                "Введите название нового ресторана:\n" +
                "Для отмены введите 'отмена'");
        }

        private async Task HandleAddRestaurantName(Message msg)
        {
            if (!_userStates.TryGetValue(msg.From.Id, out var userState))
            {
                await ShowManagerMainMenu(msg);
                return;
            }

            string restaurantName = msg.Text.Trim();

            // Проверка на отмену
            if (restaurantName.ToLower() == "отмена")
            {
                userState.StateType = "main";
                await ShowManagerMainMenu(msg);
                return;
            }

            if (string.IsNullOrWhiteSpace(restaurantName))
            {
                await _botClient.SendMessage(msg.Chat,
                    "❌ Название ресторана не может быть пустым.\n" +
                    "Попробуйте еще раз или введите 'отмена':");
                return;
            }

            try
            {
                using (var db = new MenuDatabaseManager())
                {
                    // Проверяем, существует ли уже такой ресторан
                    var restaurants = db.GetRestaurants();
                    if (restaurants.Contains(restaurantName))
                    {
                        await _botClient.SendMessage(msg.Chat,
                            $"⚠️ Ресторан '{restaurantName}' уже существует.\n\n" +
                            $"Вы можете добавить блюда для него через меню '🍽️ Добавить блюдо'.");

                        userState.StateType = "main";

                        var duplicateKeyboard = new InlineKeyboardMarkup(new[]
                        {
                            new[]
                            {
                                InlineKeyboardButton.WithCallbackData("🍽️ Добавить блюдо", "addMenuItem"),
                                InlineKeyboardButton.WithCallbackData("🏪 Добавить другой ресторан", "addRestaurant")
                            },
                            new[]
                            {
                                InlineKeyboardButton.WithCallbackData("🏠 В главное меню", "backToManagerMain")
                            }
                        });

                        await _botClient.SendMessage(msg.Chat, "Выберите действие:", replyMarkup: duplicateKeyboard);
                        return;
                    }

                    // Добавляем ресторан
                    bool success = db.AddRestaurant(restaurantName);

                    if (success)
                    {
                        await _botClient.SendMessage(msg.Chat,
                            $"✅ Ресторан '{restaurantName}' успешно добавлен!\n\n" +
                            $"Теперь вы можете:\n" +
                            $"1. Добавить блюда через меню '🍽️ Добавить блюдо'\n" +
                            $"2. Сотрудники смогут выбирать этот ресторан при заказе");
                    }
                    else
                    {
                        await _botClient.SendMessage(msg.Chat,
                            $"❌ Не удалось добавить ресторан '{restaurantName}'.\n" +
                            $"Попробуйте еще раз или проверьте логи.");
                    }
                }

                userState.StateType = "main";

                // Показываем кнопки для дальнейших действий
                var successKeyboard = new InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("🍽️ Добавить блюдо", "addMenuItem"),
                        InlineKeyboardButton.WithCallbackData("➕ Добавить еще ресторан", "addRestaurant")
                    },
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("🏠 В главное меню", "backToManagerMain")
                    }
                });

                await _botClient.SendMessage(msg.Chat, "Выберите действие:", replyMarkup: successKeyboard);
            }
            catch (Exception ex)
            {
                await _botClient.SendMessage(msg.Chat,
                    $"❌ Ошибка при добавлении ресторана: {ex.Message}\n\n" +
                    $"Попробуйте еще раз или обратитесь к разработчику.");

                userState.StateType = "main";
                await ShowManagerMainMenu(msg);
            }
        }

        // 3. Добавление блюд в меню - ПРОСТАЯ ВЕРСИЯ
        private async Task StartAddMenuItem(CallbackQuery query)
        {
            if (!_userStates.TryGetValue(query.From.Id, out var userState))
            {
                _userStates[query.From.Id] = new UserState();
                userState = _userStates[query.From.Id];
            }

            userState.StateType = "adding_menu_item_details";
            userState.TempData = null;

            await _botClient.SendMessage(query.Message.Chat,
                "🍽️ Добавление нового блюда\n\n" +
                "Введите данные в ОДНОЙ строке через запятую:\n" +
                "**Формат:** Название ресторана, Название блюда, Цена, Категория\n\n" +
                "**Пример:**\n" +
                "`Пиццерия Марио, Пицца Маргарита, 450, Основные блюда`\n\n" +
                "**Еще примеры:**\n" +
                "`Суши-бар, Ролл Филадельфия, 600, Роллы`\n" +
                "`Кофейня, Капучино, 150, Напитки`\n" +
                "`Бургерная, Чизбургер, 300, Бургеры`\n\n" +
                "ℹ️ **ВАЖНО:** Если ресторана нет - он будет создан автоматически!\n\n" +
                "Для отмены введите 'отмена'");
        }

        private async Task HandleAddMenuItem(Message msg)
        {
            if (!_userStates.TryGetValue(msg.From.Id, out var userState))
            {
                await ShowManagerMainMenu(msg);
                return;
            }

            try
            {
                string input = msg.Text.Trim();

                // Проверка на отмену
                if (input.ToLower() == "отмена")
                {
                    userState.StateType = "main";
                    await ShowManagerMainMenu(msg);
                    return;
                }

                // Разбиваем ввод на части
                var parts = input.Split(',')
                    .Select(p => p.Trim())
                    .Where(p => !string.IsNullOrEmpty(p))
                    .ToArray();

                if (parts.Length != 4)
                {
                    await _botClient.SendMessage(msg.Chat,
                        "❌ Неверный формат данных!\n\n" +
                        "Должно быть ровно 4 части через запятую:\n" +
                        "1. Название ресторана\n" +
                        "2. Название блюда\n" +
                        "3. Цена (только число)\n" +
                        "4. Категория\n\n" +
                        "**Правильный пример:**\n" +
                        "`Пиццерия Марио, Пицца Маргарита, 450, Основные блюда`\n\n" +
                        "Попробуйте еще раз или введите 'отмена':");
                    return;
                }

                string restaurantName = parts[0];
                string itemName = parts[1];
                string priceStr = parts[2];
                string category = parts[3];

                // Проверяем цену
                if (!decimal.TryParse(priceStr, out decimal price) || price <= 0)
                {
                    await _botClient.SendMessage(msg.Chat,
                        "❌ Неверная цена! Введите положительное число.\n" +
                        "Пример: 450 или 150.50\n\n" +
                        "Попробуйте еще раз или введите 'отмена':");
                    return;
                }

                // Добавляем блюдо в базу данных
                using (var db = new MenuDatabaseManager())
                {
                    bool success = db.AddMenuItem(restaurantName, itemName, price, category);

                    if (success)
                    {
                        // Проверяем, был ли создан новый ресторан
                        var restaurants = db.GetRestaurants();
                        bool isNewRestaurant = restaurants.Contains(restaurantName);

                        if (isNewRestaurant)
                        {
                            await _botClient.SendMessage(msg.Chat,
                                $"✅ Блюдо успешно добавлено!\n" +
                                $"🏪 **Ресторан создан автоматически:** {restaurantName}\n" +
                                $"🍽️ Название: {itemName}\n" +
                                $"💰 Цена: {price:C}\n" +
                                $"📂 Категория: {category}\n\n" +
                                $"✅ Теперь сотрудники могут заказывать из нового ресторана!");
                        }
                        else
                        {
                            await _botClient.SendMessage(msg.Chat,
                                $"✅ Блюдо успешно добавлено!\n\n" +
                                $"🏪 Ресторан: {restaurantName}\n" +
                                $"🍽️ Название: {itemName}\n" +
                                $"💰 Цена: {price:C}\n" +
                                $"📂 Категория: {category}\n\n" +
                                $"✅ Теперь сотрудники могут заказывать это блюдо.");
                        }
                    }
                    else
                    {
                        await _botClient.SendMessage(msg.Chat,
                            $"❌ Не удалось добавить блюдо.\n" +
                            $"Возможно, такое блюдо уже существует в этой категории.\n\n" +
                            $"Ресторан: {restaurantName}\n" +
                            $"Категория: {category}\n\n" +
                            $"Попробуйте другое название или цену.");
                    }
                }

                // Сбрасываем состояние
                userState.StateType = "main";
                userState.TempData = null;

                // Показываем главное меню с кнопкой добавления еще
                var successKeyboard = new InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("➕ Добавить еще блюдо", "addMenuItem"),
                        InlineKeyboardButton.WithCallbackData("🏪 Добавить ресторан", "addRestaurant")
                    },
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("🏠 В главное меню", "backToManagerMain")
                    }
                });

                await _botClient.SendMessage(msg.Chat, "Выберите действие:", replyMarkup: successKeyboard);
            }
            catch (Exception ex)
            {
                await _botClient.SendMessage(msg.Chat,
                    $"❌ Произошла ошибка: {ex.Message}\n\n" +
                    $"Попробуйте еще раз или обратитесь к разработчику.");

                userState.StateType = "main";
                userState.TempData = null;
                await ShowManagerMainMenu(msg);
            }
        }

        // Метод для выбора ресторана (оставляем на всякий случай, но не используем)
        private async Task HandleAddMenuItemRestaurantSelection(Message msg)
        {
            // Этот метод больше не нужен, но оставляем для совместимости
            await _botClient.SendMessage(msg.Chat,
                "ℹ️ Используйте новый формат добавления блюд:\n\n" +
                "Введите: `Название ресторана, Название блюда, Цена, Категория`\n\n" +
                "Пример: `Пиццерия Марио, Пицца Маргарита, 450, Основные блюда`");

            if (_userStates.TryGetValue(msg.From.Id, out var userState))
            {
                userState.StateType = "main";
            }

            await ShowManagerMainMenu(msg);
        }

        // 4. Установка дедлайна
        private async Task StartSetDeadline(CallbackQuery query)
        {
            if (!_userStates.TryGetValue(query.From.Id, out var userState))
            {
                _userStates[query.From.Id] = new UserState();
                userState = _userStates[query.From.Id];
            }

            userState.StateType = "setting_deadline";

            string currentDeadline = _deadlineTime != DateTime.MinValue ?
                $"Текущий дедлайн: {_deadlineTime:HH:mm}\n" :
                "Дедлайн не установлен\n";

            await _botClient.SendMessage(query.Message.Chat,
                $"{currentDeadline}\nВведите время дедлайна в формате ЧЧ:ММ (например, 18:30):\n" +
                "Для отмены введите 'отмена'");
        }

        private async Task HandleSetDeadline(Message msg)
        {
            if (!_userStates.TryGetValue(msg.From.Id, out var userState))
            {
                await ShowManagerMainMenu(msg);
                return;
            }

            string input = msg.Text.Trim();

            // Проверка на отмену
            if (input.ToLower() == "отмена")
            {
                userState.StateType = "main";
                await ShowManagerMainMenu(msg);
                return;
            }

            if (DateTime.TryParseExact(input, "HH:mm", null, System.Globalization.DateTimeStyles.None, out DateTime deadline))
            {
                _deadlineTime = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day,
                    deadline.Hour, deadline.Minute, 0);

                // Проверяем, не прошел ли дедлайн сегодня
                var now = DateTime.Now;
                if (now >= _deadlineTime)
                {
                    _isDeadlinePassed = true;
                }
                else
                {
                    _isDeadlinePassed = false;
                }

                await _botClient.SendMessage(msg.Chat,
                    $"✅ Дедлайн установлен на {_deadlineTime:HH:mm}\n" +
                    $"Статус: {(_isDeadlinePassed ? "⚠️ ПРОЙДЕН" : "✅ АКТИВЕН")}");

                userState.StateType = "main";
                await ShowManagerMainMenu(msg);
            }
            else
            {
                await _botClient.SendMessage(msg.Chat,
                    "❌ Неверный формат времени. Введите время в формате ЧЧ:ММ (например, 18:30)\n" +
                    "Или введите 'отмена' для отмены:");
            }
        }

        // 5. Пополнение баланса конкретного сотрудника
        private async Task StartTopupBalance(CallbackQuery query)
        {
            if (!_userStates.TryGetValue(query.From.Id, out var userState))
            {
                _userStates[query.From.Id] = new UserState();
                userState = _userStates[query.From.Id];
            }

            userState.StateType = "topup_employee_id";
            userState.TempData = null;

            await _botClient.SendMessage(query.Message.Chat,
                "💰 Пополнение баланса сотрудника\n\n" +
                "Введите Telegram ID сотрудника:\n" +
                "Для отмены введите 'отмена'");
        }

        private async Task HandleTopupEmployeeId(Message msg)
        {
            if (!_userStates.TryGetValue(msg.From.Id, out var userState))
            {
                await ShowManagerMainMenu(msg);
                return;
            }

            string input = msg.Text.Trim();

            // Проверка на отмену
            if (input.ToLower() == "отмена")
            {
                userState.StateType = "main";
                await ShowManagerMainMenu(msg);
                return;
            }

            if (long.TryParse(input, out long telegramId) && telegramId > 0)
            {
                using (var db = new EmployeesDatabaseManager())
                {
                    var employee = db.GetEmployeeByTelegramId(telegramId);
                    if (employee == null)
                    {
                        await _botClient.SendMessage(msg.Chat,
                            $"❌ Сотрудник с Telegram ID {telegramId} не найден.");
                        userState.StateType = "main";
                        await ShowManagerMainMenu(msg);
                        return;
                    }

                    userState.TempData = telegramId.ToString(); // Сохраняем ID
                    userState.StateType = "topup_employee_amount";

                    await _botClient.SendMessage(msg.Chat,
                        $"Сотрудник найден:\n" +
                        $"👤 Имя: {employee.Name}\n" +
                        $"💰 Текущий баланс: {employee.Amount:C}\n\n" +
                        "Введите сумму для пополнения (положительное число):\n" +
                        "Пример: 500.00\n" +
                        "Для отмены введите 'отмена'");
                }
            }
            else
            {
                await _botClient.SendMessage(msg.Chat,
                    "❌ Неверный формат Telegram ID. Введите положительное число или 'отмена':");
            }
        }

        private async Task HandleTopupEmployeeAmount(Message msg)
        {
            if (!_userStates.TryGetValue(msg.From.Id, out var userState) || string.IsNullOrEmpty(userState.TempData))
            {
                await ShowManagerMainMenu(msg);
                return;
            }

            string input = msg.Text.Trim();

            // Проверка на отмену
            if (input.ToLower() == "отмена")
            {
                userState.StateType = "main";
                userState.TempData = null;
                await ShowManagerMainMenu(msg);
                return;
            }

            if (decimal.TryParse(input, out decimal amount) && amount > 0)
            {
                long telegramId = long.Parse(userState.TempData);

                try
                {
                    using (var db = new EmployeesDatabaseManager())
                    {
                        var employee = db.GetEmployeeByTelegramId(telegramId);
                        if (employee == null)
                        {
                            await _botClient.SendMessage(msg.Chat, "❌ Сотрудник не найден.");
                            userState.StateType = "main";
                            await ShowManagerMainMenu(msg);
                            return;
                        }

                        // Сохраняем старый баланс для сообщения
                        decimal oldBalance = employee.Amount;

                        // Пополняем баланс
                        employee.Amount += amount;
                        db.UpdateEmployee(employee);

                        var message = "✅ Баланс успешно пополнен!\n\n";
                        message += $"👤 Сотрудник: {employee.Name}\n";
                        message += $"🆔 Telegram ID: {telegramId}\n";
                        message += $"💰 Пополнено: +{amount:C}\n";
                        message += $"📊 Было: {oldBalance:C}\n";
                        message += $"📈 Стало: {employee.Amount:C}\n\n";
                        message += $"✅ Операция выполнена успешно!";

                        await _botClient.SendMessage(msg.Chat, message);

                        // Уведомляем сотрудника
                        try
                        {
                            await _botClient.SendMessage(
                                chatId: telegramId,
                                text: $"💰 Ваш баланс пополнен!\n" +
                                      $"Пополнено: +{amount:C}\n" +
                                      $"Новый баланс: {employee.Amount:C}"
                            );
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Не удалось уведомить сотрудника {telegramId}: {ex.Message}");
                        }
                    }

                    userState.StateType = "main";
                    userState.TempData = null;

                    // Показываем кнопки для дальнейших действий
                    var successKeyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[]
                        {
                            InlineKeyboardButton.WithCallbackData("💰 Пополнить еще", "topupBalance"),
                            InlineKeyboardButton.WithCallbackData("💰 Пополнить всем", "topupAllEmployees")
                        },
                        new[]
                        {
                            InlineKeyboardButton.WithCallbackData("🏠 В главное меню", "backToManagerMain")
                        }
                    });

                    await _botClient.SendMessage(msg.Chat, "Выберите действие:", replyMarkup: successKeyboard);
                }
                catch (Exception ex)
                {
                    await _botClient.SendMessage(msg.Chat,
                        $"❌ Ошибка при пополнении баланса: {ex.Message}");

                    userState.StateType = "main";
                    await ShowManagerMainMenu(msg);
                }
            }
            else
            {
                await _botClient.SendMessage(msg.Chat,
                    "❌ Неверный формат суммы. Введите положительное число (например: 500.00) или 'отмена':");
            }
        }

        // 6. Пополнение баланса всем сотрудникам
        private async Task StartTopupAllEmployees(CallbackQuery query)
        {
            if (!_userStates.TryGetValue(query.From.Id, out var userState))
            {
                _userStates[query.From.Id] = new UserState();
                userState = _userStates[query.From.Id];
            }

            userState.StateType = "topup_all_amount";
            userState.TempData = null;

            await _botClient.SendMessage(query.Message.Chat,
                "💰 Пополнение баланса ВСЕМ сотрудникам\n\n" +
                "Введите сумму для пополнения (положительное число):\n" +
                "Пример: 500.00\n\n" +
                "⚠️ Внимание: эта операция пополнит баланс ВСЕМ сотрудникам в системе!\n" +
                "Для отмена введите 'отмена'");
        }

        private async Task HandleTopupAllEmployeesAmount(Message msg)
        {
            if (!_userStates.TryGetValue(msg.From.Id, out var userState))
            {
                await ShowManagerMainMenu(msg);
                return;
            }

            string input = msg.Text.Trim();

            // Проверка на отмену
            if (input.ToLower() == "отмена")
            {
                userState.StateType = "main";
                await ShowManagerMainMenu(msg);
                return;
            }

            if (decimal.TryParse(input, out decimal amount) && amount > 0)
            {
                try
                {
                    using (var db = new EmployeesDatabaseManager())
                    {
                        var employees = db.GetAllEmployees();

                        if (employees.Count == 0)
                        {
                            await _botClient.SendMessage(msg.Chat, "❌ В системе нет сотрудников.");
                            userState.StateType = "main";
                            await ShowManagerMainMenu(msg);
                            return;
                        }

                        int updatedCount = 0;
                        decimal totalAdded = 0;

                        // Пополняем баланс всем сотрудникам
                        foreach (var employee in employees)
                        {
                            employee.Amount += amount;
                            db.UpdateEmployee(employee);
                            updatedCount++;
                            totalAdded += amount;

                            // Уведомляем сотрудника
                            if (employee.TelegramId > 0)
                            {
                                try
                                {
                                    await _botClient.SendMessage(
                                        chatId: employee.TelegramId,
                                        text: $"💰 Ваш баланс пополнен!\n" +
                                              $"Пополнено: +{amount:C}\n" +
                                              $"Новый баланс: {employee.Amount:C}"
                                    );
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Не удалось уведомить сотрудника {employee.Name}: {ex.Message}");
                                }
                            }
                        }

                        var message = "✅ Баланс пополнен всем сотрудникам!\n\n";
                        message += $"👥 Количество сотрудников: {updatedCount}\n";
                        message += $"💰 Сумма на сотрудника: +{amount:C}\n";
                        message += $"💰 Общая пополненная сумма: {totalAdded:C}\n\n";
                        message += $"✅ Все сотрудники уведомлены о пополнении!";

                        await _botClient.SendMessage(msg.Chat, message);
                    }

                    userState.StateType = "main";
                    userState.TempData = null;

                    // Показываем кнопки для дальнейших действий
                    var successKeyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[]
                        {
                            InlineKeyboardButton.WithCallbackData("💰 Пополнить конкретного", "topupBalance"),
                            InlineKeyboardButton.WithCallbackData("💰 Пополнить всем еще раз", "topupAllEmployees")
                        },
                        new[]
                        {
                            InlineKeyboardButton.WithCallbackData("🏠 В главное меню", "backToManagerMain")
                        }
                    });

                    await _botClient.SendMessage(msg.Chat, "Выберите действие:", replyMarkup: successKeyboard);
                }
                catch (Exception ex)
                {
                    await _botClient.SendMessage(msg.Chat,
                        $"❌ Ошибка при пополнении баланса: {ex.Message}");

                    userState.StateType = "main";
                    await ShowManagerMainMenu(msg);
                }
            }
            else
            {
                await _botClient.SendMessage(msg.Chat,
                    "❌ Неверный формат суммы. Введите положительное число (например: 500.00) или 'отмена':");
            }
        }

        // 7. Запрос отчета
        private async Task RequestReport(CallbackQuery query)
        {
            try
            {
                // Показываем сообщение о генерации отчета
                await _botClient.SendMessage(query.Message.Chat, "📊 Генерирую отчет...");

                using (var ordersDb = new OrdersDatabaseManager())
                {
                    var organizer = new OrderOrganizer(ordersDb);
                    var reportManager = new ReportManager(organizer);

                    // Генерируем отчет
                    string reportPath = $"report_{DateTime.Now:yyyyMMdd_HHmmss}.html";
                    reportManager.SaveHtmlReport(reportPath, $"Отчет {DateTime.Now:dd.MM.yyyy HH:mm}");

                    // Отправляем отчет как файл
                    using (var stream = System.IO.File.OpenRead(reportPath))
                    {
                        await _botClient.SendDocument(
                            chatId: query.From.Id,
                            document: InputFile.FromStream(stream, Path.GetFileName(reportPath)),
                            caption: $"📊 Отчет по заказам за {DateTime.Now:dd.MM.yyyy HH:mm}"
                        );
                    }

                    // Удаляем временный файл
                    File.Delete(reportPath);

                    // Показываем кнопку возврата
                    var returnKeyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("🏠 В главное меню", "backToManagerMain") }
                    });

                    await _botClient.SendMessage(query.Message.Chat, "✅ Отчет успешно сгенерирован и отправлен!", replyMarkup: returnKeyboard);
                }
            }
            catch (Exception ex)
            {
                await _botClient.SendMessage(query.Message.Chat,
                    $"❌ Ошибка при генерации отчета: {ex.Message}\n\n" +
                    $"Попробуйте еще раз или обратитесь к разработчику.");
            }
        }
        #endregion

        public void StopBot()
        {
            _cts?.Cancel();
            _deadlineTimer?.Stop();
            _midnightTimer?.Stop();
        }
    }
}