using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace order_bot
{
    public class ReportManager
    {
        private readonly OrderOrganizer _organizer;

        public ReportManager(OrderOrganizer organizer)
        {
            _organizer = organizer ?? throw new ArgumentNullException(nameof(organizer));
        }

        public string GenerateHtmlReport(string title = "Отчет по заказам")
        {
            var stats = _organizer.GetFullStatistics();
            decimal grandTotal = stats.Sum(r => r.Value.TotalRevenue);
            int totalOrders = stats.Sum(r => r.Value.TotalOrders);
            int totalRestaurants = stats.Count;

            var html = new StringBuilder();

            html.AppendLine("<!DOCTYPE html>");
            html.AppendLine("<html lang='ru'>");
            html.AppendLine("<head>");
            html.AppendLine("    <meta charset='UTF-8'>");
            html.AppendLine($"    <title>{title}</title>");
            html.AppendLine("    <style>");
            html.AppendLine("        * { margin: 0; padding: 0; box-sizing: border-box; font-family: Arial, sans-serif; }");
            html.AppendLine("        body { background: #f5f7fa; padding: 20px; }");
            html.AppendLine("        .container { max-width: 1200px; margin: 0 auto; background: white; border-radius: 10px; box-shadow: 0 0 20px rgba(0,0,0,0.1); }");
            html.AppendLine("        .header { background: linear-gradient(to right, #4a6baf, #6a8bff); color: white; padding: 25px; text-align: center; border-radius: 10px 10px 0 0; }");
            html.AppendLine("        .header h1 { font-size: 28px; margin-bottom: 10px; }");
            html.AppendLine("        .header .date { opacity: 0.9; }");
            html.AppendLine("        .content { padding: 25px; }");
            html.AppendLine("        .restaurant-section { margin-bottom: 25px; border: 1px solid #e0e0e0; border-radius: 8px; overflow: hidden; }");
            html.AppendLine("        .restaurant-header { background: #4a6baf; color: white; padding: 15px; display: flex; justify-content: space-between; align-items: center; }");
            html.AppendLine("        .restaurant-name { font-size: 18px; font-weight: bold; }");
            html.AppendLine("        .restaurant-total { font-size: 16px; font-weight: bold; }");
            html.AppendLine("        table { width: 100%; border-collapse: collapse; }");
            html.AppendLine("        th { background: #f8f9fa; padding: 12px 15px; text-align: left; color: #555; border-bottom: 2px solid #e0e0e0; }");
            html.AppendLine("        td { padding: 12px 15px; border-bottom: 1px solid #eee; }");
            html.AppendLine("        .item-name { color: #333; font-weight: 500; }");
            html.AppendLine("        .price, .count, .amount { text-align: right; }");
            html.AppendLine("        .grand-total { background: #4a6baf; color: white; padding: 25px; text-align: center; border-radius: 8px; margin-top: 30px; }");
            html.AppendLine("        .grand-total h2 { font-size: 22px; margin-bottom: 10px; }");
            html.AppendLine("        .grand-total-value { font-size: 36px; font-weight: bold; margin: 15px 0; }");
            html.AppendLine("        .footer { text-align: center; padding: 20px; color: #777; font-size: 14px; border-top: 1px solid #eee; }");
            html.AppendLine("    </style>");
            html.AppendLine("</head>");
            html.AppendLine("<body>");
            html.AppendLine("    <div class='container'>");

            // Шапка
            html.AppendLine("        <div class='header'>");
            html.AppendLine($"            <h1>{title}</h1>");
            html.AppendLine($"            <div class='date'>Дата: {DateTime.Now:dd.MM.yyyy HH:mm}</div>");
            html.AppendLine("        </div>");

            // Контент
            html.AppendLine("        <div class='content'>");

            // По каждому ресторану
            foreach (var restaurant in stats.OrderBy(r => r.Key))
            {
                html.AppendLine("            <div class='restaurant-section'>");
                html.AppendLine("                <div class='restaurant-header'>");
                html.AppendLine($"                    <div class='restaurant-name'>{restaurant.Key}</div>");
                html.AppendLine($"                    <div class='restaurant-total'>{restaurant.Value.TotalRevenue:C}</div>");
                html.AppendLine("                </div>");

                html.AppendLine("                <table>");
                html.AppendLine("                    <thead>");
                html.AppendLine("                        <tr>");
                html.AppendLine("                            <th class='item-name'>Позиция</th>");
                html.AppendLine("                            <th class='price'>Цена</th>");
                html.AppendLine("                            <th class='count'>Количество</th>");
                html.AppendLine("                            <th class='amount'>Сумма</th>");
                html.AppendLine("                        </tr>");
                html.AppendLine("                    </thead>");
                html.AppendLine("                    <tbody>");

                foreach (var position in restaurant.Value.Positions.Values.OrderBy(p => p.ItemName))
                {
                    html.AppendLine("                        <tr>");
                    html.AppendLine($"                            <td class='item-name'>{position.ItemName}</td>");
                    html.AppendLine($"                            <td class='price'>{position.Price:C}</td>");
                    html.AppendLine($"                            <td class='count'>{position.Count}</td>");
                    html.AppendLine($"                            <td class='amount'>{position.TotalAmount:C}</td>");
                    html.AppendLine("                        </tr>");
                }

                html.AppendLine("                    </tbody>");
                html.AppendLine("                </table>");
                html.AppendLine("            </div>");
            }

            // Общая сумма
            html.AppendLine("            <div class='grand-total'>");
            html.AppendLine("                <h2>ОБЩАЯ СУММА ПО ВСЕМ РЕСТОРАНАМ</h2>");
            html.AppendLine($"                <div class='grand-total-value'>{grandTotal:C}</div>");
            html.AppendLine($"                <div>{totalRestaurants} ресторана • {totalOrders} заказов</div>");
            html.AppendLine("            </div>");

            html.AppendLine("        </div>");

            // Подвал
            html.AppendLine("        <div class='footer'>");
            html.AppendLine($"            <p>Отчет сгенерирован автоматически • {DateTime.Now:dd.MM.yyyy HH:mm}</p>");
            html.AppendLine("        </div>");

            html.AppendLine("    </div>");
            html.AppendLine("</body>");
            html.AppendLine("</html>");

            return html.ToString();
        }

        public void SaveHtmlReport(string filePath, string title = "Отчет по заказам")
        {
            string html = GenerateHtmlReport(title);
            File.WriteAllText(filePath, html, Encoding.UTF8);
        }

        public void OpenReportInBrowser(string filePath = "report.html", string title = "Отчет по заказам")
        {
            SaveHtmlReport(filePath, title);

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = Path.GetFullPath(filePath),
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Не удалось открыть отчет: {ex.Message}");
            }
        }
    }
}
