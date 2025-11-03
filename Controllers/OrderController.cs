using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Linq;

namespace WebApplication1.Controllers
{
    public class OrderController : Controller
    {
        // ✅ 資料庫連線字串
        private readonly string connStr =
            Environment.GetEnvironmentVariable("DB_CONNECTION") ??
            "Server=220.132.119.146\\SQLEXPRESS;Database=Asia_wms;User Id=SA_02;Password=0912888237;TrustServerCertificate=True;";


        // ✅ 購物車暫存（收入與支出分開）
        private static List<CartItem> Cart = new();

        // ========================== 收入 POS ==========================
        [HttpGet]
        public IActionResult POS()
        {
            var menu = LoadMenuItems("收入");
            ViewBag.Menu = menu;
            ViewBag.Cart = Cart;
            return View();
        }

        // ========================== 支出 POS ==========================
        [HttpGet]
        public IActionResult ExpensePOS()
        {
            var menu = LoadMenuItems("支出");
            ViewBag.Menu = menu;
            ViewBag.Cart = Cart;
            return View("ExpensePOS");
        }

        // ✅ 共用讀取方法
        private Dictionary<string, List<MenuItem>> LoadMenuItems(string incomeType)
        {
            var menu = new Dictionary<string, List<MenuItem>>();

            using (var conn = new SqlConnection(connStr))
            {
                conn.Open();
                string sql = @"
                    SELECT Category, Name, Price, ExtraNote, ExtraPrice 
                    FROM MenuItems 
                    WHERE IncomeType = @type
                    ORDER BY Category, auto_no";

                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@type", incomeType);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string category = reader["Category"]?.ToString() ?? string.Empty;
                            string name = reader["Name"]?.ToString() ?? string.Empty;
                            decimal price = Convert.ToDecimal(reader["Price"]);
                            string extraNote = reader["ExtraNote"]?.ToString() ?? string.Empty;
                            decimal extraPrice = reader["ExtraPrice"] == DBNull.Value ? 0 : Convert.ToDecimal(reader["ExtraPrice"]);

                            if (!menu.ContainsKey(category))
                                menu[category] = new List<MenuItem>();

                            menu[category].Add(new MenuItem
                            {
                                Name = name,
                                Price = price,
                                ExtraNote = extraNote,
                                ExtraPrice = extraPrice
                            });
                        }
                    }
                }
            }

            return menu;
        }

        // ========================== 購物車操作 ==========================

        [HttpPost]
        public IActionResult AddToCart(string name, decimal price, string? extraNote, decimal? extraPrice)
        {
            string fullName = string.IsNullOrEmpty(extraNote) ? name : $"{name} + {extraNote}";
            decimal finalPrice = price + (extraPrice ?? 0);

            var exist = Cart.FirstOrDefault(x => x.Name == fullName);
            if (exist == null)
                Cart.Add(new CartItem { Name = fullName, Price = finalPrice, Quantity = 1 });
            else
                exist.Quantity++;

            return Ok(); // ✅ 不重整頁面
        }

        [HttpPost]
        public IActionResult UpdateQuantity(string name, int quantity)
        {
            var item = Cart.FirstOrDefault(x => x.Name == name);
            if (item != null && quantity > 0)
                item.Quantity = quantity;

            return Redirect(Request.Headers["Referer"].ToString());
        }

        [HttpPost]
        public IActionResult RemoveItem(string name)
        {
            var item = Cart.FirstOrDefault(x => x.Name == name);
            if (item != null)
                Cart.Remove(item);

            return Redirect(Request.Headers["Referer"].ToString());
        }

        [HttpPost]
        public IActionResult ClearCart()
        {
            Cart.Clear();
            return Redirect(Request.Headers["Referer"].ToString());
        }

        // ========================== 收入結帳 ==========================
        [HttpPost]
        public IActionResult Checkout()
        {
            if (Cart.Count == 0)
            {
                TempData["Message"] = "⚠️ 沒有任何點餐資料。";
                return RedirectToAction("POS");
            }

            SaveOrder(Cart, "收入");
            Cart.Clear();

            TempData["Message"] = "✅ 收入結帳成功！";
            return RedirectToAction("POS");
        }

        // ========================== 支出結帳 ==========================
        [HttpPost]
        public IActionResult ExpenseCheckout()
        {
            if (Cart.Count == 0)
            {
                TempData["Message"] = "⚠️ 沒有任何支出資料。";
                return RedirectToAction("ExpensePOS");
            }

            SaveOrder(Cart, "支出");
            Cart.Clear();

            TempData["Message"] = "✅ 支出已成功記錄！";
            return RedirectToAction("ExpensePOS");
        }

        // ✅ 共用存檔方法
        private void SaveOrder(List<CartItem> cart, string type)
        {
            using (var conn = new SqlConnection(connStr))
            {
                conn.Open();

                string today = DateTime.Now.ToString("yyyy-MM-dd");
                string countSql = "SELECT COUNT(DISTINCT order_number) FROM POS_Order_History WHERE order_date = @date AND IncomeType = @type";
                int orderCount = 0;

                using (var cmd = new SqlCommand(countSql, conn))
                {
                    cmd.Parameters.AddWithValue("@date", today);
                    cmd.Parameters.AddWithValue("@type", type);
                    orderCount = Convert.ToInt32(cmd.ExecuteScalar());
                }

                string prefix = type == "支出" ? "EXP-" : "INC-";
                string orderNumber = prefix + DateTime.Now.ToString("yyyyMMdd") + "-" + (orderCount + 1).ToString("D3");

                foreach (var item in cart)
                {
                    string insertSql = @"
                        INSERT INTO POS_Order_History
                        (order_date, order_number, item_name, price, quantity, subtotal, IncomeType)
                        VALUES (@date, @number, @name, @price, @qty, @subtotal, @type)";
                    using (var cmd = new SqlCommand(insertSql, conn))
                    {
                        cmd.Parameters.AddWithValue("@date", today);
                        cmd.Parameters.AddWithValue("@number", orderNumber);
                        cmd.Parameters.AddWithValue("@name", item.Name ?? "");
                        cmd.Parameters.AddWithValue("@price", item.Price);
                        cmd.Parameters.AddWithValue("@qty", item.Quantity);
                        cmd.Parameters.AddWithValue("@subtotal", item.Subtotal);
                        cmd.Parameters.AddWithValue("@type", type);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
        }

        // ========================== 今日收支查詢 ==========================
        [HttpGet]
        public IActionResult TodayOrders()
        {
            var todayOrders = new List<OrderRecord>();

            using (var conn = new SqlConnection(connStr))
            {
                conn.Open();

                string sql = @"
                    SELECT order_number, item_name, price, quantity, subtotal, IncomeType
                    FROM POS_Order_History
                    WHERE order_date = @today
                    ORDER BY IncomeType, order_number";

                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@today", DateTime.Now.ToString("yyyy-MM-dd"));
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            todayOrders.Add(new OrderRecord
                            {
                                OrderNumber = reader["order_number"].ToString(),
                                ItemName = reader["item_name"].ToString(),
                                Price = Convert.ToDecimal(reader["price"]),
                                Quantity = Convert.ToInt32(reader["quantity"]),
                                Subtotal = Convert.ToDecimal(reader["subtotal"]),
                                IncomeType = reader["IncomeType"].ToString()
                            });
                        }
                    }
                }
            }

            ViewBag.TodayOrders = todayOrders;
            return View();
        }

        [HttpGet]
        public IActionResult CartPartial(string viewType = "POS")
        {
            ViewBag.ViewType = viewType; // ✅ 傳入目前頁面類型
            return PartialView("_CartPartial", Cart);
        }

        [HttpPost]
        public IActionResult DeleteOrder(string orderNumber)
        {
            using (var conn = new SqlConnection(connStr))
            {
                conn.Open();
                string sql = "DELETE FROM POS_Order_History WHERE order_number = @num";
                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@num", orderNumber);
                    cmd.ExecuteNonQuery();
                }
            }

            TempData["Message"] = $"🗑 已刪除訂單 {orderNumber}";
            return RedirectToAction("TodayOrders");
        }

        // ========================== 模型 ==========================
        public class MenuItem
        {
            public string? Name { get; set; }
            public decimal Price { get; set; }
            public string? ExtraNote { get; set; }
            public decimal ExtraPrice { get; set; }
        }

        public class CartItem
        {
            public string? Name { get; set; }
            public decimal Price { get; set; }
            public int Quantity { get; set; }
            public decimal Subtotal => Price * Quantity;
        }

        public class OrderRecord
        {
            public string? OrderNumber { get; set; }
            public string? ItemName { get; set; }
            public decimal Price { get; set; }
            public int Quantity { get; set; }
            public decimal Subtotal { get; set; }
            public string? IncomeType { get; set; }
        }
    }
}