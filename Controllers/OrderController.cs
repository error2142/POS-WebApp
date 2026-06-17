using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace WebApplication1.Controllers
{
    public class OrderController : Controller
    {
        private readonly string connStr =
        Environment.GetEnvironmentVariable("DB_CONNECTION") ??
        "Data Source=220.132.119.146,1433;Initial Catalog=Asia_wms;User Id=SA_02;Password=0912888237;TrustServerCertificate=True;";

        // ========================== 🔥 Session購物車 ==========================

        private List<CartItem> GetCart()
        {
            var json = HttpContext.Session.GetString("Cart");
            return string.IsNullOrEmpty(json)
                ? new List<CartItem>()
                : JsonSerializer.Deserialize<List<CartItem>>(json);
        }

        private void SaveCart(List<CartItem> cart)
        {
            HttpContext.Session.SetString("Cart", JsonSerializer.Serialize(cart));
        }

        private void ClearCartSession()
        {
            HttpContext.Session.Remove("Cart");
        }

        // ========================== POS ==========================

        [HttpGet]
        public IActionResult POS()
        {
            if (HttpContext.Session.GetString("user") == null)
                return RedirectToAction("Index", "Login");

            ViewBag.Menu = LoadMenuItems("收入");
            ViewBag.Cart = GetCart();
            ViewBag.Customers = LoadCustomers();
            ViewBag.DefaultDate = DateTime.Now.ToString("yyyy-MM-dd");

            return View();
        }

        private List<string> LoadCustomers()
        {
            var list = new List<string>();

            using var conn = new SqlConnection(connStr);
            conn.Open();

            var cmd = new SqlCommand("SELECT customer_number FROM Customer_table ORDER BY customer_number", conn);
            var reader = cmd.ExecuteReader();

            while (reader.Read())
                list.Add(reader["customer_number"].ToString());

            return list;
        }

        private Dictionary<string, List<MenuItem>> LoadMenuItems(string type)
        {
            var menu = new Dictionary<string, List<MenuItem>>();

            using var conn = new SqlConnection(connStr);
            conn.Open();

            string sql = @"SELECT Category, Name, Price, ExtraNote, ExtraPrice 
                           FROM MenuItems 
                           WHERE IncomeType=@type
                           ORDER BY Category, auto_no";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@type", type);

            var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                string cat = reader["Category"]?.ToString() ?? "";
                if (!menu.ContainsKey(cat))
                    menu[cat] = new List<MenuItem>();

                menu[cat].Add(new MenuItem
                {
                    Name = reader["Name"]?.ToString(),
                    Price = Convert.ToDecimal(reader["Price"]),
                    ExtraNote = reader["ExtraNote"]?.ToString(),
                    ExtraPrice = reader["ExtraPrice"] == DBNull.Value ? 0 : Convert.ToDecimal(reader["ExtraPrice"])
                });
            }

            return menu;
        }

        // ========================== 購物車 ==========================

        [HttpPost]
        public IActionResult AddToCart(string name, decimal price, string? extraNote, decimal? extraPrice)
        {
            var cart = GetCart();

            string fullName = string.IsNullOrEmpty(extraNote) ? name : $"{name} + {extraNote}";
            decimal finalPrice = price + (extraPrice ?? 0);

            var exist = cart.FirstOrDefault(x => x.Name == fullName);

            if (exist == null)
                cart.Add(new CartItem { Name = fullName, Price = finalPrice, Quantity = 1 });
            else
                exist.Quantity++;

            SaveCart(cart);
            return Ok();
        }

        [HttpPost]
        public IActionResult UpdateQuantity(string name, int quantity)
        {
            var cart = GetCart();

            var item = cart.FirstOrDefault(x => x.Name == name);
            if (item != null && quantity > 0)
                item.Quantity = quantity;

            SaveCart(cart);
            return Redirect(Request.Headers["Referer"].ToString());
        }

        [HttpPost]
        public IActionResult RemoveItem(string name)
        {
            var cart = GetCart();

            var item = cart.FirstOrDefault(x => x.Name == name);
            if (item != null)
                cart.Remove(item);

            SaveCart(cart);

            return Ok(); // 🔥 改這裡
        }

        [HttpPost]
        public IActionResult ClearCart()
        {
            ClearCartSession();
            return Redirect(Request.Headers["Referer"].ToString());
        }

        // ========================== 結帳 ==========================

        [HttpPost]
        public IActionResult Checkout(string customer, string orderDate, string pickupLocation, string remark)
        {
            var cart = GetCart();

            if (cart.Count == 0)
            {
                TempData["Message"] = "⚠️ 沒有商品";
                return RedirectToAction("POS");
            }

            SaveOrder(cart, "收入", customer, orderDate, pickupLocation, remark);

            ClearCartSession();

            TempData["Message"] = $"完成！客戶:{customer}";
            return RedirectToAction("POS");
        }

        private void SaveOrder(List<CartItem> cart, string type, string customer, string orderDate, string pickupLocation, string remark)
        {
            using var conn = new SqlConnection(connStr);
            conn.Open();

            string userId = HttpContext.Session.GetString("user") ?? "";
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            string orderNumber = DateTime.Now.ToString("yyyyMMddHHmmssfff");

            foreach (var item in cart)
            {
                string sql = @"INSERT INTO POS_Order_History
                (order_date, order_number, item_name, price, quantity, subtotal, IncomeType, customer_number, shipping_customer_number, delivery_date, id, commentary)
                VALUES (@date,@num,@name,@price,@qty,@sub,@type,@cust,@shipcust,@del,@id,@commentary)";

                using var cmd = new SqlCommand(sql, conn);

                cmd.Parameters.AddWithValue("@date", today);
                cmd.Parameters.AddWithValue("@num", orderNumber);
                cmd.Parameters.AddWithValue("@name", item.Name ?? "");
                cmd.Parameters.AddWithValue("@price", item.Price);
                cmd.Parameters.AddWithValue("@qty", item.Quantity);
                cmd.Parameters.AddWithValue("@sub", item.Subtotal);
                cmd.Parameters.AddWithValue("@type", type);
                cmd.Parameters.AddWithValue("@cust", customer ?? "");
                cmd.Parameters.AddWithValue("@shipcust", pickupLocation ?? "");
                cmd.Parameters.AddWithValue("@del", orderDate ?? "");
                cmd.Parameters.AddWithValue("@id", userId);
                cmd.Parameters.AddWithValue("@commentary", string.IsNullOrWhiteSpace(remark) ? "" : remark);

                cmd.ExecuteNonQuery();
            }
        }

        // ========================== 查詢 ==========================

        [HttpGet]
        public IActionResult TodayOrders()
        {
            var list = new List<OrderRecord>();

            using var conn = new SqlConnection(connStr);
            conn.Open();

            string userId = HttpContext.Session.GetString("user") ?? "";

            string sql = @"SELECT h.*,c.customer_name
                   FROM POS_Order_History h
                   LEFT JOIN Customer_table c
                   ON h.customer_number=c.customer_number
                   WHERE h.id = @id
                   AND CONVERT(date, h.delivery_date) = CONVERT(date, GETDATE())";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", userId);

            var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                list.Add(new OrderRecord
                {
                    OrderNumber = reader["order_number"].ToString(),
                    ItemName = reader["item_name"].ToString(),
                    Price = Convert.ToDecimal(reader["price"]),
                    Quantity = Convert.ToInt32(reader["quantity"]),
                    Subtotal = Convert.ToDecimal(reader["subtotal"]),
                    Customer = reader["customer_number"].ToString(),
                    CustomerName = reader["customer_name"].ToString(),
                    DeliveryDate = reader["delivery_date"].ToString(),
                    IncomeType = reader["IncomeType"].ToString()
                });
            }

            ViewBag.TodayOrders = list;
            return View();
        }

        [HttpGet]
        public IActionResult CartPartial()
        {
            return PartialView("_CartPartial", GetCart());
        }

        [HttpGet]
        public IActionResult SearchCustomer(string keyword)
        {
            var list = new List<string>();

            using var conn = new SqlConnection(connStr);
            conn.Open();

            string sql = @"SELECT TOP 20 customer_number + ' - ' + customer_name AS display
                           FROM Customer_table
                           WHERE customer_number LIKE @kw OR customer_name LIKE @kw";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@kw", "%" + keyword + "%");

            var reader = cmd.ExecuteReader();

            while (reader.Read())
                list.Add(reader["display"].ToString());

            return Json(list);
        }
        [HttpPost]
        public IActionResult ChangeQty(string name, int delta)
        {
            var cart = GetCart();

            var item = cart.FirstOrDefault(x => x.Name == name);

            if (item != null)
            {
                item.Quantity += delta;

                // 🔥 如果變 0 就刪掉
                if (item.Quantity <= 0)
                    cart.Remove(item);
            }

            SaveCart(cart);

            return Ok();
        }

        [HttpPost]
        public IActionResult DeleteOrder(string orderNumber)
        {
            using var conn = new SqlConnection(connStr);
            conn.Open();

            string sql = "DELETE FROM POS_Order_History WHERE order_number = @num";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@num", orderNumber);

            cmd.ExecuteNonQuery();

            TempData["Message"] = $"已刪除訂單 {orderNumber}";

            return RedirectToAction("TodayOrders");
        }

        // ========================== Model ==========================

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
            public string OrderNumber { get; set; }
            public string ItemName { get; set; }
            public decimal Price { get; set; }
            public int Quantity { get; set; }
            public decimal Subtotal { get; set; }
            public string Customer { get; set; }
            public string CustomerName { get; set; }
            public string DeliveryDate { get; set; }
            public string IncomeType { get; set; }
        }
    }
}