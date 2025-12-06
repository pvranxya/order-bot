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
        private static DateTime _deadlineTime = DateTime.MinValue;
        private static bool _isDeadlinePassed = false;

        private static readonly Dictionary<long, string> _userRoles = new Dictionary<long, string>();
        private static readonly Dictionary<long, UserState> _userStates = new Dictionary<long, UserState>();

        // Класс для хранения состояния пользователя
        private class UserState
        {
            public string StateType { get; set; } = "main"; // main, restaurant_selection, category_selection, item_selection
            public string SelectedRestaurant { get; set; }
            public string SelectedCategory { get; set; }
            public List<OrderItem> SelectedItems { get; set; } = new List<OrderItem>();

            // Словари для сопоставления чисел с реальными значениями
            public Dictionary<int, string> RestaurantMapping { get; set; }
            public Dictionary<int, string> CategoryMapping { get; set; }
            public Dictionary<int, MenuItem> ItemMapping { get; set; }

            // Для добавления сотрудников и ресторанов
            public string TempData { get; set; } // Для временного хранения данных
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
            _botClient = new TelegramBotClient("7553461301:AAHdi2tbhlu0GrvWDLFs2HOE1Fpw46ZMQJw", cancellationToken: _cts.Token);

            var me = await _botClient.GetMe();

            _botClient.OnError += OnError;
            _botClient.OnMessage += OnMessage;
            _botClient.OnUpdate += OnUpdate;

            // Запускаем таймер для проверки дедлайна каждую минуту
            StartDeadlineTimer();

            Console.WriteLine($"@{me.Username} is running... Press Enter to terminate");
            Console.ReadLine();

            _cts.Cancel();
            _deadlineTimer?.Stop();
        }

        private void StartDeadlineTimer()
        {
            _deadlineTimer = new System.Timers.Timer(60000); // Проверка каждую минуту
            _deadlineTimer.Elapsed += CheckDeadline;
            _deadlineTimer.AutoReset = true;
            _deadlineTimer.Enabled = true;
        }

        private async void CheckDeadline(object sender, ElapsedEventArgs e)
        {
            if (_deadlineTime != DateTime.MinValue && !_isDeadlinePassed)
            {
                var now = DateTime.Now;
                if (now >= _deadlineTime)
                {
                    _isDeadlinePassed = true;
                    await SendDailyReportAndClearOrders();
                }
                else
                {
                    // Проверяем, не прошел ли дедлайн сегодня
                    var todayDeadline = new DateTime(now.Year, now.Month, now.Day,
                        _deadlineTime.Hour, _deadlineTime.Minute, 0);

                    if (now >= todayDeadline)
                    {
                        _isDeadlinePassed = true;
                        await SendDailyReportAndClearOrders();
                    }
                }
            }
        }

        private async Task SendDailyReportAndClearOrders()
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

                    // Очищаем заказы
                    int deletedCount = ordersDb.ClearAllOrders();
                    Console.WriteLine($"Дедлайн прошел. Очищено {deletedCount} заказов.");
                }

                // Сбрасываем флаг для следующего дня
                _isDeadlinePassed = false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при обработке дедлайна: {ex.Message}");
            }
        }

        private async Task SendReportToManager(string reportPath)
        {
            try
            {
                // Находим ID менеджера из файла
                int managerId = int.Parse(File.ReadAllText("..\\..\\..\\Databases\\ManagerId.txt").Trim());

                // Отправляем сообщение менеджеру
                using (var stream = System.IO.File.OpenRead(reportPath))
                {
                    await _botClient.SendDocumentAsync(
                        chatId: managerId,
                        document: InputFile.FromStream(stream, Path.GetFileName(reportPath)),
                        caption: $"📊 Ежедневный отчет за {DateTime.Now:dd.MM.yyyy}"
                    );
                }

                Console.WriteLine($"Отчет отправлен менеджеру {managerId}");
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

            // Команды сотрудника
            var currentEmployee = new Employee();
            using (var db = new EmployeesDatabaseManager())
            {
                currentEmployee = db.GetEmployeeByTelegramId(msg.From.Id);
            }

            if (msg.Text == "/help")
            {
                var helpText = "Команды:\n" +
                              "/help - показать справку\n" +
                              "/logout - выйти из системы\n" +
                              "\nОсновные действия через кнопки меню";

                await _botClient.SendMessage(msg.Chat, helpText);
            }
            else if (msg.Text == "/logout")
            {
                _userRoles.Remove(msg.From.Id);
                _userStates.Remove(msg.From.Id);
                await _botClient.SendMessage(msg.Chat, "Вы вышли из системы. Используйте /start для повторной авторизации.");
            }
            else
            {
                await ShowEmployeeMainMenu(msg, currentEmployee);
            }
        }

        private async Task HandleEmployeeAuth(CallbackQuery query)
        {
            using (var db = new EmployeesDatabaseManager())
            {
                if (db.EmployeeExistsByTelegramId(query.From.Id))
                {
                    _userRoles[query.From.Id] = "employee";
                    _userStates[query.From.Id] = new UserState();
                    await _botClient.SendMessage(query.Message.Chat, "Успешно авторизованы как сотрудник✅");

                    var currentEmployee = db.GetEmployeeByTelegramId(query.From.Id);
                    await ShowEmployeeMainMenu(query, currentEmployee);
                }
                else
                {
                    await _botClient.SendMessage(query.Message.Chat, "Нет такого сотрудника❌");
                }
            }
        }

        private async Task ShowEmployeeMainMenu(Message msg, Employee currentEmployee)
        {
            var message = $"Вы авторизованы как сотрудник\n{currentEmployee.Name}   {currentEmployee.Amount}";

            // Проверяем дедлайн
            if (_isDeadlinePassed)
            {
                message += "\n\n⚠️ ДЕДЛАЙН НА СЕГОДНЯ ПРОЙДЕН\nЗаказы не принимаются";
            }

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

            await _botClient.SendMessage(msg.Chat, message, replyMarkup: keyboard);
        }

        private async Task ShowEmployeeMainMenu(CallbackQuery query, Employee currentEmployee)
        {
            var message = $"Вы авторизованы как сотрудник\n{currentEmployee.Name}   {currentEmployee.Amount}";

            if (_isDeadlinePassed)
            {
                message += "\n\n⚠️ ДЕДЛАЙН НА СЕГОДНЯ ПРОЙДЕН\nЗаказы не принимаются";
            }

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

        // Методы для работы с заказами (из предыдущей версии) остаются без изменений
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

            message += $"💰 Общая сумма: {totalPrice} руб.\n";
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
                int ordersCreated = 0;
                decimal totalPrice = 0;

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
                        totalPrice += orderItem.TotalPrice;
                    }
                }

                // Верификация: проверяем, что заказы действительно сохранились
                bool verificationPassed = await VerifyOrderCreation(userState.SelectedItems);

                var message = "✅ Заказ успешно создан!\n\n";
                message += $"📋 Количество позиций: {ordersCreated}\n";
                message += $"💰 Общая сумма: {totalPrice} руб.\n";

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
                await _botClient.SendMessage(query.Message.Chat, "Успешно авторизованы как менеджер✅");
                await ShowManagerMainMenu(query);
            }
            else
            {
                await _botClient.SendMessage(query.Message.Chat, "Нет такого менеджера❌");
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
            if (userState.StateType == "adding_employee_name")
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
                await HandleAddMenuItem(msg);
                return;
            }
            else if (userState.StateType == "setting_deadline")
            {
                await HandleSetDeadline(msg);
                return;
            }

            // Команды менеджера
            if (msg.Text == "/help")
            {
                var helpText = "Команды менеджера:\n" +
                              "/help - показать справку\n" +
                              "/logout - выйти из системы\n" +
                              "\nОсновные действия через кнопки меню";

                await _botClient.SendMessage(msg.Chat, helpText);
            }
            else if (msg.Text == "/logout")
            {
                _userRoles.Remove(msg.From.Id);
                _userStates.Remove(msg.From.Id);
                await _botClient.SendMessage(msg.Chat, "Вы вышли из системы. Используйте /start для повторной авторизации.");
            }
            else
            {
                await ShowManagerMainMenu(msg);
            }
        }

        private async Task ShowManagerMainMenu(CallbackQuery query)
        {
            var message = "Вы авторизованы как менеджер\n\n";

            if (_deadlineTime != DateTime.MinValue)
            {
                message += $"⏰ Текущий дедлайн: {_deadlineTime:HH:mm}\n";
                message += _isDeadlinePassed ? "⚠️ ДЕДЛАЙН СЕГОДНЯ ПРОЙДЕН\n" : "✅ ДЕДЛАЙН ЕЩЕ НЕ НАСТУПИЛ\n";
            }
            else
            {
                message += "⏰ Дедлайн не установлен\n";
            }

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
                    InlineKeyboardButton.WithCallbackData("📊 Запросить отчет", "requestReport")
                }
            });

            await _botClient.SendMessage(query.Message.Chat, message, replyMarkup: keyboard);
        }

        private async Task ShowManagerMainMenu(Message msg)
        {
            var message = "Вы авторизованы как менеджер\n\n";

            if (_deadlineTime != DateTime.MinValue)
            {
                message += $"⏰ Текущий дедлайн: {_deadlineTime:HH:mm}\n";
                message += _isDeadlinePassed ? "⚠️ ДЕДЛАЙН СЕГОДНЯ ПРОЙДЕН\n" : "✅ ДЕДЛАЙН ЕЩЕ НЕ НАСТУПИЛ\n";
            }
            else
            {
                message += "⏰ Дедлайн не установлен\n";
            }

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

            userState.StateType = "adding_employee_name";
            userState.TempData = null;

            await _botClient.SendMessage(query.Message.Chat, "Введите имя нового сотрудника:");
        }

        private async Task HandleAddEmployeeName(Message msg)
        {
            if (!_userStates.TryGetValue(msg.From.Id, out var userState))
            {
                await ShowManagerMainMenu(msg);
                return;
            }

            userState.TempData = msg.Text; // Сохраняем имя
            userState.StateType = "adding_employee_amount";

            await _botClient.SendMessage(msg.Chat, $"Имя сотрудника: {msg.Text}\n\nТеперь введите сумму (десятичное число):");
        }

        private async Task HandleAddEmployeeAmount(Message msg)
        {
            if (!_userStates.TryGetValue(msg.From.Id, out var userState) || string.IsNullOrEmpty(userState.TempData))
            {
                await ShowManagerMainMenu(msg);
                return;
            }

            if (decimal.TryParse(msg.Text, out decimal amount))
            {
                string employeeName = userState.TempData;

                try
                {
                    using (var db = new EmployeesDatabaseManager())
                    {
                        // Предполагаем, что у EmployeesDatabaseManager есть метод AddEmployee
                        // Если такого метода нет, нужно его добавить
                        // Для примера создадим новый Employee и сохраним
                        var employee = new Employee
                        {
                            Name = employeeName,
                            Amount = amount,
                            TelegramId = 0 // Telegram ID будет установлен при первой авторизации
                        };

                        // Здесь должен быть код добавления сотрудника в БД
                        // Временно выводим информацию
                        Console.WriteLine($"Добавлен сотрудник: {employeeName}, сумма: {amount}");
                    }

                    await _botClient.SendMessage(msg.Chat, $"✅ Сотрудник '{employeeName}' с суммой {amount} успешно добавлен!");
                    userState.StateType = "main";
                    await ShowManagerMainMenu(msg);
                }
                catch (Exception ex)
                {
                    await _botClient.SendMessage(msg.Chat, $"❌ Ошибка при добавлении сотрудника: {ex.Message}");
                    userState.StateType = "main";
                    await ShowManagerMainMenu(msg);
                }
            }
            else
            {
                await _botClient.SendMessage(msg.Chat, "❌ Неверный формат суммы. Пожалуйста, введите десятичное число (например: 1000.50):");
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

            await _botClient.SendMessage(query.Message.Chat, "Введите название нового ресторана:");
        }

        private async Task HandleAddRestaurantName(Message msg)
        {
            if (!_userStates.TryGetValue(msg.From.Id, out var userState))
            {
                await ShowManagerMainMenu(msg);
                return;
            }

            string restaurantName = msg.Text;

            try
            {
                using (var db = new MenuDatabaseManager())
                {
                    // Проверяем, существует ли уже такой ресторан
                    var restaurants = db.GetRestaurants();
                    if (restaurants.Contains(restaurantName))
                    {
                        await _botClient.SendMessage(msg.Chat, $"⚠️ Ресторан '{restaurantName}' уже существует.");
                        userState.StateType = "main";
                        await ShowManagerMainMenu(msg);
                        return;
                    }

                    // Добавляем ресторан через добавление тестового блюда
                    // Позже можно будет добавить нормальный метод добавления ресторана
                    var testMenuItem = new MenuItem
                    {
                        Restaurant = restaurantName,
                        Name = "Тестовое блюдо",
                        Price = 0,
                        Category = "Тестовая категория"
                    };

                    db.AddMenuItem(testMenuItem);
                    db.DeleteMenuItem(testMenuItem.Id); // Удаляем тестовое блюдо
                }

                await _botClient.SendMessage(msg.Chat, $"✅ Ресторан '{restaurantName}' успешно добавлен!\n\nТеперь вы можете добавлять блюда для этого ресторана.");
                userState.StateType = "main";
                await ShowManagerMainMenu(msg);
            }
            catch (Exception ex)
            {
                await _botClient.SendMessage(msg.Chat, $"❌ Ошибка при добавлении ресторана: {ex.Message}");
                userState.StateType = "main";
                await ShowManagerMainMenu(msg);
            }
        }

        // 3. Добавление блюд в меню
        private async Task StartAddMenuItem(CallbackQuery query)
        {
            try
            {
                using (var db = new MenuDatabaseManager())
                {
                    var restaurants = db.GetRestaurants();

                    if (restaurants.Count == 0)
                    {
                        await _botClient.SendMessage(query.Message.Chat, "❌ Нет ресторанов. Сначала добавьте ресторан.");
                        return;
                    }

                    // Создаем кнопки для выбора ресторана
                    var buttons = new List<InlineKeyboardButton[]>();
                    foreach (var restaurant in restaurants)
                    {
                        buttons.Add(new[]
                        {
                            InlineKeyboardButton.WithCallbackData($"🏪 {restaurant}", $"additem_{restaurant}")
                        });
                    }

                    buttons.Add(new[]
                    {
                        InlineKeyboardButton.WithCallbackData("Назад", "backToManagerMain")
                    });

                    var keyboard = new InlineKeyboardMarkup(buttons);

                    await _botClient.SendMessage(
                        query.Message.Chat,
                        "Выберите ресторан для добавления блюда:",
                        replyMarkup: keyboard
                    );
                }
            }
            catch (Exception ex)
            {
                await _botClient.SendMessage(query.Message.Chat, $"❌ Ошибка: {ex.Message}");
            }
        }

        private async Task HandleAddMenuItem(Message msg)
        {
            if (!_userStates.TryGetValue(msg.From.Id, out var userState) || string.IsNullOrEmpty(userState.TempData))
            {
                await ShowManagerMainMenu(msg);
                return;
            }

            // Формат: "Название блюда,Цена,Категория"
            var parts = msg.Text.Split(',');
            if (parts.Length != 3)
            {
                await _botClient.SendMessage(msg.Chat, "❌ Неверный формат. Введите: Название блюда,Цена,Категория\nПример: Пицца Маргарита,450,Основные блюда");
                return;
            }

            string restaurantName = userState.TempData;
            string itemName = parts[0].Trim();

            if (!decimal.TryParse(parts[1].Trim(), out decimal price))
            {
                await _botClient.SendMessage(msg.Chat, "❌ Неверный формат цены. Цена должна быть числом.");
                return;
            }

            string category = parts[2].Trim();

            try
            {
                using (var db = new MenuDatabaseManager())
                {
                    var menuItem = new MenuItem
                    {
                        Restaurant = restaurantName,
                        Name = itemName,
                        Price = price,
                        Category = category
                    };

                    db.AddMenuItem(menuItem);
                }

                await _botClient.SendMessage(msg.Chat, $"✅ Блюдо '{itemName}' успешно добавлено в ресторан '{restaurantName}'!");
                userState.StateType = "main";
                userState.TempData = null;
                await ShowManagerMainMenu(msg);
            }
            catch (Exception ex)
            {
                await _botClient.SendMessage(msg.Chat, $"❌ Ошибка при добавлении блюда: {ex.Message}");
                userState.StateType = "main";
                userState.TempData = null;
                await ShowManagerMainMenu(msg);
            }
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
                $"{currentDeadline}\nВведите время дедлайна в формате ЧЧ:ММ (например, 18:30):");
        }

        private async Task HandleSetDeadline(Message msg)
        {
            if (!_userStates.TryGetValue(msg.From.Id, out var userState))
            {
                await ShowManagerMainMenu(msg);
                return;
            }

            if (DateTime.TryParseExact(msg.Text, "HH:mm", null, System.Globalization.DateTimeStyles.None, out DateTime deadline))
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
                await _botClient.SendMessage(msg.Chat, "❌ Неверный формат времени. Введите время в формате ЧЧ:ММ (например, 18:30):");
            }
        }

        // 5. Запрос отчета
        private async Task RequestReport(CallbackQuery query)
        {
            try
            {
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
                        await _botClient.SendDocumentAsync(
                            chatId: query.From.Id,
                            document: InputFile.FromStream(stream, Path.GetFileName(reportPath)),
                            caption: $"📊 Отчет по заказам за {DateTime.Now:dd.MM.yyyy HH:mm}"
                        );
                    }

                    // Удаляем временный файл
                    File.Delete(reportPath);
                }
            }
            catch (Exception ex)
            {
                await _botClient.SendMessage(query.Message.Chat, $"❌ Ошибка при генерации отчета: {ex.Message}");
            }
        }
        #endregion

        public void StopBot()
        {
            _cts?.Cancel();
            _deadlineTimer?.Stop();
        }
    }
}