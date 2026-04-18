using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

public class LoginController : Controller
{
    // 🔥 直接用你這段
    private readonly string connStr =
    Environment.GetEnvironmentVariable("DB_CONNECTION") ??
    "Data Source=220.132.119.146,1433;Initial Catalog=User_information;User Id=SA_02;Password=0912888237;TrustServerCertificate=True;";

    public IActionResult Index()
    {
        return View();
    }

    [HttpPost]
    public IActionResult Login(string username, string password)
    {
        using (var conn = new SqlConnection(connStr))
        {
            conn.Open();

            string sql = @"
SELECT COUNT(*)
FROM User_information.dbo.Account_management
WHERE User_ID = @user
AND Password = @pwd";

            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@user", username);
                cmd.Parameters.AddWithValue("@pwd", password);

                int count = (int)cmd.ExecuteScalar();

                if (count > 0)
                {
                    // ✅ 登入成功
                    HttpContext.Session.SetString("user", username);

                    return RedirectToAction("POS", "Order");
                }
            }
        }

        ViewBag.Error = "帳號或密碼錯誤";
        return View("Index");
    }

    public IActionResult Logout()
    {
        HttpContext.Session.Clear();
        return RedirectToAction("Index");
    }
}