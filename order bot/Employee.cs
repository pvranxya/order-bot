using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace order_bot
{
    public class Employee
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public long TelegramId { get; set; }
        public decimal Amount { get; set; }
        public string Office { get; set; }
    }
}
