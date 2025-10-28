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
    Environment.GetEnvironmentVariable("DB_CONNECTION") ?? "";
        // ✅ 購物車暫存
        private static List<CartItem> Cart = new();

        // ✅ 顯示 POS 點餐頁面
        [HttpGet]
        public IActionResult POS()
        {
            var menu = new Dictionary<string, List<MenuItem>>();

            using (var conn = new SqlConnection(connStr))
            {
                conn.Open();
                string sql = "SELECT Category, Name, Price FROM MenuItems ORDER BY Category, auto_no";

                using (var cmd = new SqlCommand(sql, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string category = reader["Category"]?.ToString() ?? string.Empty;
                        string name = reader["Name"]?.ToString() ?? string.Empty;
                        decimal price = Convert.ToDecimal(reader["Price"]);

                        if (!menu.ContainsKey(category))
                            menu[category] = new List<MenuItem>();

                        menu[category].Add(new MenuItem { Name = name, Price = price });
                    }
                }
            }

            ViewBag.Menu = menu;
            ViewBag.Cart = Cart;
            return View();
        }

        // ✅ 加入購物車
        [HttpPost]
        public IActionResult AddToCart(string name, decimal price)
        {
            var exist = Cart.FirstOrDefault(x => x.Name == name);
            if (exist == null)
                Cart.Add(new CartItem { Name = name, Price = price, Quantity = 1 });
            else
                exist.Quantity++;

            return RedirectToAction("POS");
        }

        // ✅ 清空購物車
        [HttpPost]
        public IActionResult ClearCart()
        {
            Cart.Clear();
            return RedirectToAction("POS");
        }

        // ✅ 刪除單筆項目
        [HttpPost]
        public IActionResult RemoveItem(string name)
        {
            var item = Cart.FirstOrDefault(x => x.Name == name);
            if (item != null)
                Cart.Remove(item);

            return RedirectToAction("POS");
        }

        // ✅ 修改購物車數量
        [HttpPost]
        public IActionResult UpdateQuantity(string name, int quantity)
        {
            var item = Cart.FirstOrDefault(x => x.Name == name);
            if (item != null && quantity > 0)
                item.Quantity = quantity;

            return RedirectToAction("POS");
        }

        // ✅ 模型
        public class MenuItem
        {
            public string? Name { get; set; }
            public decimal Price { get; set; }
        }

        public class CartItem
        {
            public string? Name { get; set; }
            public decimal Price { get; set; }
            public int Quantity { get; set; }
            public decimal Subtotal => Price * Quantity;
        }

        [HttpPost]
        public IActionResult Checkout()
        {
            if (Cart.Count == 0)
            {
                TempData["Message"] = "⚠️ 沒有任何點餐資料。";
                return RedirectToAction("POS");
            }

            using (var conn = new SqlConnection(connStr))
            {
                conn.Open();

                string today = DateTime.Now.ToString("yyyy-MM-dd");

                // 查詢今天已有幾張單
                string countSql = "SELECT COUNT(DISTINCT order_number) FROM POS_Order_History WHERE order_date = @date";
                int orderCount = 0;
                using (var cmd = new SqlCommand(countSql, conn))
                {
                    cmd.Parameters.AddWithValue("@date", today);
                    orderCount = Convert.ToInt32(cmd.ExecuteScalar());
                }

                // 產生單號：例如 20250302-001
                string orderNumber = DateTime.Now.ToString("yyyyMMdd") + "-" + (orderCount + 1).ToString("D3");

                // 寫入購物車每一筆資料
                foreach (var item in Cart)
                {
                    string insertSql = @"
                INSERT INTO POS_Order_History
                (order_date, order_number, item_name, price, quantity, subtotal)
                VALUES (@date, @number, @name, @price, @qty, @subtotal)";
                    using (var cmd = new SqlCommand(insertSql, conn))
                    {
                        cmd.Parameters.AddWithValue("@date", today);
                        cmd.Parameters.AddWithValue("@number", orderNumber);
                        cmd.Parameters.AddWithValue("@name", item.Name ?? "");
                        cmd.Parameters.AddWithValue("@price", item.Price);
                        cmd.Parameters.AddWithValue("@qty", item.Quantity);
                        cmd.Parameters.AddWithValue("@subtotal", item.Subtotal);
                        cmd.ExecuteNonQuery();
                    }
                }
            }

            // ✅ 結帳成功清空購物車
            Cart.Clear();
            TempData["Message"] = "✅ 結帳成功，資料已寫入資料庫！";
            return RedirectToAction("POS");
        }

        [HttpGet]
        public IActionResult TodayOrders()
        {
            var todayOrders = new List<OrderRecord>();

            using (var conn = new SqlConnection(connStr))
            {
                conn.Open();

                string sql = @"
            SELECT order_number, item_name, price, quantity, subtotal
            FROM POS_Order_History
            WHERE order_date = @today
            ORDER BY order_number";
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
                                Subtotal = Convert.ToDecimal(reader["subtotal"])
                            });
                        }
                    }
                }
            }

            ViewBag.TodayOrders = todayOrders;
            return View();
        }

        // ✅ 刪除單筆訂單
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

        // ✅ 用於顯示的模型
        public class OrderRecord
        {
            public string? OrderNumber { get; set; }
            public string? ItemName { get; set; }
            public decimal Price { get; set; }
            public int Quantity { get; set; }
            public decimal Subtotal { get; set; }
        }

    }
}
