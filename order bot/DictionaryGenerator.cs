using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace order_bot
{
    internal class RestaurantDictionaryGenerator
    {
        public Dictionary<int, string> CreateDictionaryFromArray(string[] items)
        {
            var dictionary = new Dictionary<int, string>();

            if (items == null || items.Length == 0)
            {
                return dictionary;
            }

            for (int i = 0; i < items.Length; i++)
            {
                int number = i + 1;
                dictionary[number] = items[i];
            }

            return dictionary;
        }

        public Dictionary<int, string> CreateDictionaryFromList(List<string> items)
        {
            var dictionary = new Dictionary<int, string>();

            if (items == null || items.Count == 0)
            {
                return dictionary;
            }

            for (int i = 0; i < items.Count; i++)
            {
                int number = i + 1;
                dictionary[number] = items[i];
            }

            return dictionary;
        }

        public string GetItemByNumber(Dictionary<int, string> dictionary, int number)
        {
            if (dictionary == null)
            {
                return null;
            }

            if (dictionary.TryGetValue(number, out string item))
            {
                return item;
            }

            return null;
        }
        public List<int> GetDictionaryKeys(Dictionary<int, string> dictionary)
        {
            if (dictionary == null)
            {
                return new List<int>();
            }

            return new List<int>(dictionary.Keys);
        }

        public List<string> GetDictionaryValues(Dictionary<int, string> dictionary)
        {
            if (dictionary == null)
            {
                return new List<string>();
            }

            return new List<string>(dictionary.Values);
        }

        public bool ContainsNumber(Dictionary<int, string> dictionary, int number)
        {
            if (dictionary == null)
            {
                return false;
            }

            return dictionary.ContainsKey(number);
        }

        public int GetDictionaryCount(Dictionary<int, string> dictionary)
        {
            if (dictionary == null)
            {
                return 0;
            }
            return dictionary.Count;
        }
    }
}