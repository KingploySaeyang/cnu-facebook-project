using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;
using System.Text;
using System.Text.Json;

namespace CnuFacebookAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    // Controller สำหรับจัดการ Facebook Webhook และการเชื่อมต่อเพจ
    public class CnuFacebookController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly IMemoryCache _cache;
        private readonly IHttpClientFactory _httpFactory;
        /*  private = ใช้ได้เฉพาะใน class นี้
            readonly = กำหนดค่าได้ครั้งเดียวใน constructor เปลี่ยนทีหลังไม่ได้*/
        private const string VERIFY_TOKEN = "CNU2025";

        //
        public CnuFacebookController(IConfiguration config, IMemoryCache cache, IHttpClientFactory httpFactory)
        {
            _config = config;
            _cache = cache;
            _httpFactory = httpFactory;
        }

        // ─────────────────────────────────────────────────────────────
        // Facebook Webhook verification — Facebook เรียก GET มาที่ endpoint นี้เพื่อตรวจสอบ
        // ─────────────────────────────────────────────────────────────
        [HttpGet]
        public IActionResult Get(
            [FromQuery(Name = "hub.mode")] string? hubMode,
            [FromQuery(Name = "hub.challenge")] string? hubChallenge,
            [FromQuery(Name = "hub.verify_token")] string? hubVerifyToken)
        {
            if (hubMode == "subscribe" && hubVerifyToken == VERIFY_TOKEN)
                return Ok(hubChallenge);

            return Unauthorized();
        }

        /*
            รับข้อความจาก Facebook แล้วให้ AI ตอบกลับ
        */
        [HttpPost]
        public IActionResult Post([FromBody] JsonElement body)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    //ดึกข้อมูล entry จาก JSON ที่ Facebook ส่งมา
                    var entry = body.GetProperty("entry")[0];
                    foreach (var messaging in entry.GetProperty("messaging").EnumerateArray())
                    {
                        // ถ้าใน messaging ไม่มี Properties ที่ชื่อว่า "message" ให้ข้าม (continue) รอบนี้ไปเลย แต่ถ้ามี ให้ดึงค่านั้นมาใส่ไว้ในตัวแปรที่ชื่อว่า message แล้วทำงานต่อ
                        if (!messaging.TryGetProperty("message", out var message)) continue;

                        /*
                            senderId = ID ของ user ที่ส่งข้อความมา
                            receiverId = ID ของ เพจ ที่รับข้อความ
                        */
                        var senderId = messaging.GetProperty("sender").GetProperty("id").GetString()!;
                        var receiverId = messaging.GetProperty("recipient").GetProperty("id").GetString()!;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Webhook background error: " + ex.Message);
                }
            });

            return Ok();
        }

        // ─────────────────────────────────────────────────────────────
        // STEP 1 — สร้าง Facebook Login URL
        // Blazor เรียก GET แล้วเปิด popup ด้วย URL ที่ได้
        // ─────────────────────────────────────────────────────────────
        [HttpGet("GetFacebookLoginUrl")]
        public IActionResult GetFacebookLoginUrl()
        {
            // ดึงค่า App ID และ Redirect URI จาก configuration
            string appId = _config["FacebookApp:AppId"]!;
            string redirectUri = Uri.EscapeDataString(_config["FacebookApp:RedirectUri"]!);

            // pages_show_list = ดึงรายการเพจ (ไม่ต้อง App Review)
            // pages_messaging = รับ-ส่ง Messenger (ต้อง App Review สำหรับ production)
            string scope = Uri.EscapeDataString(
                "pages_show_list,pages_messaging");

            // state = random GUID กันการปลอมแปลง (CSRF protection)
            string state = Uri.EscapeDataString(Guid.NewGuid().ToString("N"));
            // URL ไปยังหน้า Login ของ Facebook พร้อม query parameters ที่จำเป็น
            string url = "https://www.facebook.com/v25.0/dialog/oauth" +
                         $"?client_id={appId}" +
                         $"&redirect_uri={redirectUri}" +
                         $"&scope={scope}" +
                         $"&state={state}" +
                         "&response_type=code";

            return Ok(new { loginUrl = url });
        }

        // ─────────────────────────────────────────────────────────────
        // STEP 2 — Facebook redirect มาที่นี่พร้อม ?code=&state=
        // แลก code → short token → long-lived token (60 วัน) → ดึงเพจ
        // จากนั้น redirect ไปหน้า Blazor
        // ─────────────────────────────────────────────────────────────
        [HttpGet("FacebookCallback")]
        public async Task<IActionResult> FacebookCallback(
            [FromQuery] string? code,
            [FromQuery] string? state,
            [FromQuery] string? error)
        {
            // ดึง URL หน้า Blazor จาก configuration เพื่อใช้ในการ redirect หลังจาก process เสร็จ
            string frontendUrl = _config["FacebookApp:FrontendSelectPageUrl"]!;

            if (!string.IsNullOrEmpty(error))
                return Content(PopupRedirectHtml($"{frontendUrl}?fb_error={Uri.EscapeDataString(error)}"), "text/html");

            // ตรวจสอบว่ามี code และ state หรือไม่
            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
                return BadRequest("Invalid callback parameters.");

            string appId = _config["FacebookApp:AppId"]!;
            string appSecret = _config["FacebookApp:AppSecret"]!;
            string redirectUri = _config["FacebookApp:RedirectUri"]!;

            // สร้าง HttpClient เพื่อเรียก Facebook Graph API
            using var http = _httpFactory.CreateClient();

            // แลก authorization code → short-lived user token
            var shortTokenUrl =
                $"https://graph.facebook.com/v25.0/oauth/access_token" +
                $"?client_id={appId}" +
                $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                $"&client_secret={appSecret}" +
                $"&code={code}";

            var shortRes = await http.GetAsync(shortTokenUrl);
            if (!shortRes.IsSuccessStatusCode)// ถ้าแลก token ไม่สำเร็จ ให้ redirect ไปหน้า Blazor พร้อมแจ้ง error
            {
                string err = await shortRes.Content.ReadAsStringAsync();
                return Content(PopupRedirectHtml($"{frontendUrl}?fb_error={Uri.EscapeDataString("token_exchange_failed")}"), "text/html");
            }

            // ดึง short-lived token จาก response
            var shortJson = await shortRes.Content.ReadAsStringAsync();
            var shortDoc = JsonDocument.Parse(shortJson);
            if (!shortDoc.RootElement.TryGetProperty("access_token", out var shortTokEl))
                return Content(PopupRedirectHtml($"{frontendUrl}?fb_error={Uri.EscapeDataString("short_token_missing")}"), "text/html");
            string shortToken = shortTokEl.GetString()!;

            // แลก short → long-lived token (60 วัน)
            var longTokenUrl =
                $"https://graph.facebook.com/v25.0/oauth/access_token" +
                $"?grant_type=fb_exchange_token" +
                $"&client_id={appId}" +
                $"&client_secret={appSecret}" +
                $"&fb_exchange_token={shortToken}";

            // ดึง long-lived token จาก response
            var longRes  = await http.GetAsync(longTokenUrl);
            var longJson = await longRes.Content.ReadAsStringAsync();
            if (!longRes.IsSuccessStatusCode)
                return Content(PopupRedirectHtml($"{frontendUrl}?fb_error={Uri.EscapeDataString("long_token_failed")}"), "text/html");
            var longDoc = JsonDocument.Parse(longJson);
            if (!longDoc.RootElement.TryGetProperty("access_token", out var longTokEl))
                return Content(PopupRedirectHtml($"{frontendUrl}?fb_error={Uri.EscapeDataString("long_token_missing")}"), "text/html");
            string longToken = longTokEl.GetString()!;

            // ดึงข้อมูลผู้ใช้ที่ login (ชื่อ, รูปโปรไฟล์)
            var meUrl =
                $"https://graph.facebook.com/v25.0/me?fields=id,name,picture.type(large)&access_token={longToken}";
            // ดึงข้อมูลผู้ใช้จาก response (จะเก็บไว้ใน cache ชั่วคราว แล้วให้ Blazor ดึงจาก cache อีกที)
            var meRes = await http.GetAsync(meUrl);
            var meJson = await meRes.Content.ReadAsStringAsync();
            var meDoc = JsonDocument.Parse(meJson);// แปลง JSON string ให้อ่านค่าออกมาได้

            // ดึงข้อมูลผู้ใช้จาก response
            string fbUserId = meDoc.RootElement.TryGetProperty("id", out var uid) ? uid.GetString() ?? "" : "";
            string fbUserName = meDoc.RootElement.TryGetProperty("name", out var uname) ? uname.GetString() ?? "" : "";
            string fbUserPicture = "";
            // ดึง URL รูปโปรไฟล์จาก response
            if (meDoc.RootElement.TryGetProperty("picture", out var pic) &&
                pic.TryGetProperty("data", out var picData) &&
                picData.TryGetProperty("url", out var picUrl))
            {
                fbUserPicture = picUrl.GetString() ?? "";
            }

            // ดึงรายการเพจที่ user เป็น admin พร้อมรูปโปรไฟล์
            var pagesUrl =
                $"https://graph.facebook.com/v25.0/me/accounts" +
                $"?fields=id,name,access_token,picture.type(large)" +
                $"&access_token={longToken}";

            // ดึงข้อมูลเพจจาก response (จะเก็บไว้ใน cache ชั่วคราว แล้วให้ Blazor ดึงจาก cache อีกที)
            var pagesRes = await http.GetAsync(pagesUrl);
            var pagesJson = await pagesRes.Content.ReadAsStringAsync();

            // เก็บข้อมูลชั่วคราวใน IMemoryCache (หมดอายุ 10 นาที)
            string cacheKey = $"fb_pages_{fbUserId}_{Guid.NewGuid():N}";
            _cache.Set(cacheKey, new FacebookSessionCache
            {
                LongToken = longToken,
                PagesJson = pagesJson,
                FbUserId = fbUserId,
                FbUserName = fbUserName,
                FbUserPicture = fbUserPicture
            }, TimeSpan.FromMinutes(10));
            /*  Facebook → Backend → (ส่ง token ตรงไป Blazor ❌ ไม่ปลอดภัย)
                Facebook → Backend → Cache (RAM) → ส่งแค่ cacheKey ไป Blazor ✅*/

            return Content(PopupRedirectHtml($"{frontendUrl}?fb_session={Uri.EscapeDataString(cacheKey)}"), "text/html");
        }

        // ปิด popup แล้วนำทาง parent window ไปยัง url ที่กำหนด
        private static string PopupRedirectHtml(string url) => $@"<!DOCTYPE html>
            <html><head><meta charset=""utf-8""></head><body><script>
            if (window.opener) {{
                window.opener.location.href = '{url}';
                window.close();
            }} else {{
                window.location.href = '{url}';
            }}
            </script></body></html>";

        // ─────────────────────────────────────────────────────────────
        // ดึงข้อมูล AccessTokenFacebook ทั้งหมดของ CreateUserID = 3
        // ─────────────────────────────────────────────────────────────
        [HttpGet("GetUserOneTokens")]
        public IActionResult GetUserOneTokens()
        {
            // ดึง connection string จาก appsettings.json (key: ConnectionStrings:EMS)
            // รูปแบบ: "Server=...;Database=...;User Id=...;Password=...;"
            string connStr = _config["ConnectionStrings:EMS"]!;
            var result = new List<object>();

            // SqlConnection = ตัวเชื่อมต่อกับ SQL Server
            // using = ปิด connection อัตโนมัติเมื่อออกจาก block (ไม่ต้อง conn.Close() เอง)
            using var conn = new SqlConnection(connStr);
            conn.Open(); // เปิด connection จริงๆ (ยังไม่ query)

            // SqlCommand = คำสั่ง SQL ที่จะรัน ผูกกับ connection ที่เปิดไว้
            // @ ข้างหน้า string = verbatim string literal (เขียนหลายบรรทัดได้โดยไม่ต้อง \n)
            using var cmd = new SqlCommand(@"
                SELECT ID, AccessToken, LongLivedToken, PageID, PageName,
                       CreateUserID, CreateDate, CreateTime, UpdateUserID, UpdateDate, UpdateTime
                FROM AccessTokenFacebook
                WHERE CreateUserID = 3
                ORDER BY CreateDate DESC, CreateTime DESC", conn);

            // ExecuteReader() = รัน SELECT แล้วได้ SqlDataReader กลับมา
            // reader ทำหน้าที่เหมือน cursor ชี้ทีละแถว (ยังไม่โหลดข้อมูลทั้งหมดเข้า RAM)
            using var reader = cmd.ExecuteReader();

            // reader.Read() = เลื่อน cursor ไปแถวถัดไป คืน true ถ้ายังมีข้อมูล, false ถ้าหมดแล้ว
            while (reader.Read())
            {
                result.Add(new
                {
                    id           = Convert.ToInt64(reader["ID"]),

                    // DBNull.Value = ค่า NULL จาก SQL — ต้องเช็คก่อน ไม่งั้น .ToString() จะ crash
                    // pattern: reader["คอลัมน์"] == DBNull.Value ? ค่าแทน : แปลงค่าจริง
                    accessToken  = reader["AccessToken"]  == DBNull.Value ? "" : reader["AccessToken"].ToString(),
                    longLivedToken = reader["LongLivedToken"] == DBNull.Value ? "" : reader["LongLivedToken"].ToString(),
                    pageId       = reader["PageID"]       == DBNull.Value ? "" : reader["PageID"].ToString(),
                    pageName     = reader["PageName"]     == DBNull.Value ? "" : reader["PageName"].ToString(),
                    createUserId = reader["CreateUserID"] == DBNull.Value ? (int?)null : Convert.ToInt32(reader["CreateUserID"]),

                    // Convert.ToDateTime แล้ว .ToString("yyyy-MM-dd") = แปลงวันที่ให้อยู่ในรูป ISO 8601
                    createDate   = reader["CreateDate"]   == DBNull.Value ? "" : Convert.ToDateTime(reader["CreateDate"]).ToString("yyyy-MM-dd"),
                    createTime   = reader["CreateTime"]   == DBNull.Value ? "" : reader["CreateTime"].ToString(),
                    updateUserId = reader["UpdateUserID"] == DBNull.Value ? (int?)null : Convert.ToInt32(reader["UpdateUserID"]),
                    updateDate   = reader["UpdateDate"]   == DBNull.Value ? "" : Convert.ToDateTime(reader["UpdateDate"]).ToString("yyyy-MM-dd"),
                    updateTime   = reader["UpdateTime"]   == DBNull.Value ? "" : reader["UpdateTime"].ToString()
                });
            }

            return Ok(result);
        }

        // ─────────────────────────────────────────────────────────────
        // STEP 3 — Blazor เรียกดึงรายการเพจที่ user เป็น admin
        // ─────────────────────────────────────────────────────────────
        [HttpGet("GetFacebookPages")]
        public IActionResult GetFacebookPages([FromQuery] string sessionKey)
        {
            if (!_cache.TryGetValue(sessionKey, out FacebookSessionCache? session) || session == null)
                return BadRequest(new { message = "Session หมดอายุ กรุณา Login ใหม่" });

            var pagesDoc = JsonDocument.Parse(session.PagesJson);
            var pages = new List<object>();

            // ดึงข้อมูลเพจจาก JSON ที่เก็บใน cache แล้วส่งกลับไปให้ Blazor แสดง
            if (pagesDoc.RootElement.TryGetProperty("data", out var dataArray))
            {
                foreach (var page in dataArray.EnumerateArray())
                {
                    string pictureUrl = "";
                    if (page.TryGetProperty("picture", out var pic) &&
                        pic.TryGetProperty("data", out var picData) &&
                        picData.TryGetProperty("url", out var picUrl))
                    {
                        pictureUrl = picUrl.GetString() ?? "";
                    }
                    // ส่งข้อมูลเพจกลับไปให้ Blazor แสดง (รวม page access token ไว้ด้วย เพื่อให้ Blazor ส่งกลับมาบันทึกใน DB)
                    pages.Add(new
                    {
                        pageId = page.GetProperty("id").GetString(),
                        pageName = page.TryGetProperty("name", out var n) ? n.GetString() : "",
                        pictureUrl,
                        // ส่ง page access token เพื่อให้ Blazor ส่งกลับมาตอน save
                        pageAccessToken = page.TryGetProperty("access_token", out var t) ? t.GetString() : ""
                    });
                }
            }

            return Ok(new
            {
                fbUserId = session.FbUserId,
                longLivedToken = session.LongToken,
                sessionKey,
                pages
            });
        }

        //// ─────────────────────────────────────────────────────────────
        //// STEP 4 — Blazor ส่งเพจที่เลือกมาบันทึก โดย proxy ต่อไปหา CNUConnectApi
        //// ─────────────────────────────────────────────────────────────
        //[HttpPost("SaveSelectedPages")]
        //public async Task<IActionResult> SaveSelectedPages([FromBody] SaveSelectedPagesRequest req)
        //{
        //    if (req == null || string.IsNullOrEmpty(req.LongToken) || req.Pages == null || req.Pages.Count == 0)
        //        return BadRequest(new { message = "ข้อมูลไม่ครบ" });

        //    string baseUrl  = _config["CNUConnectApi:BaseUrl"]!;
        //    // string authUser = _config["CNUConnectApi:BasicAuthUser"]!;
        //    // string authPass = _config["CNUConnectApi:BasicAuthPassword"]!;
        //    // string basicAuth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{authUser}:{authPass}"));

        //    using var http = _httpFactory.CreateClient();
        //    // http.DefaultRequestHeaders.Authorization =
        //    //     new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", basicAuth);

        //    var saved  = new List<string>();
        //    var errors = new List<string>();

        //    foreach (var page in req.Pages)
        //    {
        //        try
        //        {
        //            var payload = new
        //            {
        //                pageId           = page.PageId,
        //                pageName         = page.PageName,
        //                pageAccessToken  = page.PageAccessToken,
        //                longToken        = req.LongToken
        //            };

        //            var content = new StringContent(
        //                System.Text.Json.JsonSerializer.Serialize(payload),
        //                Encoding.UTF8, "application/json");

        //            var res = await http.PostAsync(
        //                $"{baseUrl}/api/ApiFacebookCnu/SaveFacebookAccessToken", content);

        //            if (res.IsSuccessStatusCode)
        //                saved.Add(page.PageName);
        //            else
        //            {
        //                string err = await res.Content.ReadAsStringAsync();
        //                errors.Add($"{page.PageName}: {(int)res.StatusCode} {err}");
        //            }
        //        }
        //        catch (Exception ex)
        //        {
        //            errors.Add($"{page.PageName}: {ex.Message}");
        //        }
        //    }

        //    if (errors.Count > 0)
        //        return BadRequest(new { saved, errors });

        //    return Ok(new { message = $"บันทึกสำเร็จ {saved.Count} เพจ", saved });
        //}

        // ─────────────────────────────────────────────────────────────
        // สร้าง session จาก LongLivedToken ที่เก็บในฐานข้อมูล (ไม่ต้อง OAuth ใหม่)
        // ─────────────────────────────────────────────────────────────
        //[HttpGet("CreateSessionFromToken")]
        //public async Task<IActionResult> CreateSessionFromToken([FromQuery] string fbUserId)
        //{
        //    if (string.IsNullOrWhiteSpace(fbUserId))
        //        return BadRequest(new { message = "ต้องระบุ fbUserId" });

        //    string connStr = _config["ConnectionStrings:EMS"]!;
        //    string longToken = "";

        //    using (var conn = new SqlConnection(connStr))
        //    {
        //        conn.Open();
        //        using var cmd = new SqlCommand(@"
        //            SELECT TOP 1 LongLivedToken
        //            FROM AccessTokenFacebook
        //            WHERE CreateUserID = @uid AND LongLivedToken IS NOT NULL AND LongLivedToken <> ''
        //            ORDER BY CreateDate DESC",
        //            conn);
        //        // Parameters.AddWithValue = ใส่ค่า parameter ลงใน SQL แบบปลอดภัย (ป้องกัน SQL Injection)
        //        // @uid ใน SQL จะถูกแทนที่ด้วยค่า fbUserId จริงๆ โดย SQL Server จัดการ escape ให้เอง
        //        cmd.Parameters.AddWithValue("@uid", fbUserId);
        //        // ExecuteScalar() = รัน SELECT แล้วคืนแค่ค่าเดียว (ช่องแรก แถวแรก)
        //        // เหมาะกับ query ที่ต้องการแค่ค่าเดียว เช่น COUNT(*), MAX(), หรือ SELECT TOP 1 คอลัมน์เดียว
        //        var result = cmd.ExecuteScalar();
        //        longToken = result?.ToString() ?? "";
        //    }

        //    if (string.IsNullOrEmpty(longToken))
        //        return BadRequest(new { message = "ไม่พบ token กรุณา Login Facebook ใหม่" });

        //    using var http = _httpFactory.CreateClient();

        //    var meUrl = $"https://graph.facebook.com/v25.0/me?fields=id,name,picture.type(large)&access_token={longToken}";
        //    var meRes = await http.GetAsync(meUrl);
        //    var meJson = await meRes.Content.ReadAsStringAsync();
        //    var meDoc = JsonDocument.Parse(meJson);

        //    if (meDoc.RootElement.TryGetProperty("error", out _))
        //        return BadRequest(new { message = "Token หมดอายุ กรุณา Login Facebook ใหม่" });

        //    string fbName = meDoc.RootElement.TryGetProperty("name", out var uname) ? uname.GetString() ?? "" : "";
        //    string fbPicture = "";
        //    if (meDoc.RootElement.TryGetProperty("picture", out var pic) &&
        //        pic.TryGetProperty("data", out var picData) &&
        //        picData.TryGetProperty("url", out var picUrl))
        //    {
        //        fbPicture = picUrl.GetString() ?? "";
        //    }

        //    var pagesUrl = $"https://graph.facebook.com/v25.0/me/accounts?fields=id,name,access_token,picture.type(large)&access_token={longToken}";
        //    var pagesRes = await http.GetAsync(pagesUrl);
        //    var pagesJson = await pagesRes.Content.ReadAsStringAsync();

        //    string cacheKey = $"fb_pages_{fbUserId}_{Guid.NewGuid():N}";
        //    _cache.Set(cacheKey, new FacebookSessionCache
        //    {
        //        LongToken = longToken,
        //        PagesJson = pagesJson,
        //        FbUserId = fbUserId,
        //        FbUserName = fbName,
        //        FbUserPicture = fbPicture
        //    }, TimeSpan.FromMinutes(10));

        //    return Ok(new { sessionKey = cacheKey });
        //}

        //// ─────────────────────────────────────────────────────────────
        //// GET รายการ Token ที่บันทึกไว้ (สำหรับหน้าจัดการ)
        //// ─────────────────────────────────────────────────────────────
        //[HttpGet("GetFacebookTokens")]
        //public IActionResult GetFacebookTokens([FromQuery] string fbUserId)
        //{
        //    if (string.IsNullOrWhiteSpace(fbUserId))
        //        return BadRequest("ต้องระบุ fbUserId");

        //    string connStr = _config["ConnectionStrings:EMS"]!;
        //    var result = new List<object>();

        //    using var conn = new SqlConnection(connStr);
        //    conn.Open();

        //    using var cmd = new SqlCommand(@"
        //        SELECT ID, PageID, PageName, CreateDate, CreateTime
        //        FROM AccessTokenFacebook
        //        WHERE CreateUserID = @uid
        //        ORDER BY CreateDate DESC",
        //        conn);
        //    cmd.Parameters.AddWithValue("@uid", fbUserId);

        //    using var reader = cmd.ExecuteReader();
        //    while (reader.Read())
        //    {
        //        result.Add(new
        //        {
        //            id = reader["ID"].ToString(),
        //            pageId = reader["PageID"].ToString(),
        //            pageName = reader["PageName"].ToString(),
        //            createDate = reader["CreateDate"].ToString(),
        //            createTime = reader["CreateTime"].ToString()
        //        });
        //    }

        //    return Ok(result);
        //}

        //// ─────────────────────────────────────────────────────────────
        //// ยกเลิกการเชื่อมต่อเพจ (hard delete)
        //// ─────────────────────────────────────────────────────────────
        //[HttpPatch("DisconnectPage")]
        //public IActionResult DisconnectPage([FromBody] DisconnectPageRequest req)
        //{
        //    if (req == null || string.IsNullOrEmpty(req.PageID) || string.IsNullOrEmpty(req.FbUserId))
        //        return BadRequest("ข้อมูลไม่ครบ");

        //    string connStr = _config["ConnectionStrings:EMS"]!;

        //    using var conn = new SqlConnection(connStr);
        //    conn.Open();

        //    using var cmd = new SqlCommand(@"
        //        DELETE FROM AccessTokenFacebook
        //        WHERE CreateUserID = @uid AND PageID = @pid",
        //        conn);
        //    cmd.Parameters.AddWithValue("@uid", req.FbUserId);
        //    cmd.Parameters.AddWithValue("@pid", req.PageID);

        //    // ExecuteNonQuery() = รัน INSERT / UPDATE / DELETE (ไม่ได้คืนแถวข้อมูล)
        //    // คืนค่าเป็น int = จำนวนแถวที่ถูกกระทบ (affected rows)
        //    // ถ้า rows > 0 = ทำงานสำเร็จ, rows == 0 = ไม่เจอแถวที่ตรงเงื่อนไข
        //    int rows = cmd.ExecuteNonQuery();
        //    return rows > 0 ? Ok("ยกเลิกการเชื่อมต่อสำเร็จ") : NotFound("ไม่พบข้อมูล");
        //}

        //// ─────────────────────────────────────────────────────────────
        //// เปิด/ปิดตอบอัตโนมัติของเพจ
        //// ─────────────────────────────────────────────────────────────
        //[HttpPatch("TogglePageStatus")]
        //public IActionResult TogglePageStatus([FromBody] TogglePageStatusRequest req)
        //{
        //    if (req == null || string.IsNullOrEmpty(req.PageID) || string.IsNullOrEmpty(req.FbUserId))
        //        return BadRequest("ข้อมูลไม่ครบ");

        //    string connStr = _config["ConnectionStrings:EMS"]!;

        //    using var conn = new SqlConnection(connStr);
        //    conn.Open();

        //    using var cmd = new SqlCommand(@"
        //        UPDATE AccessTokenFacebook
        //        SET OpenStatus = @status
        //        WHERE CreateUserID = @uid AND PageID = @pid",
        //        conn);
        //    cmd.Parameters.AddWithValue("@status", req.OpenStatus);
        //    cmd.Parameters.AddWithValue("@uid", req.FbUserId);
        //    cmd.Parameters.AddWithValue("@pid", req.PageID);

        //    int rows = cmd.ExecuteNonQuery();
        //    return rows > 0 ? Ok("อัปเดตสถานะสำเร็จ") : NotFound("ไม่พบข้อมูล");
        //}

        //// ─────────────────────────────────────────────────────────────
        //// บันทึก PrompSet (บุคลิก AI) ของเพจ
        //// ─────────────────────────────────────────────────────────────
        //[HttpPatch("UpdatePrompSet")]
        //public IActionResult UpdatePrompSet([FromBody] UpdatePrompSetRequest req)
        //{
        //    if (req == null || string.IsNullOrEmpty(req.PageID) || string.IsNullOrEmpty(req.FbUserId))
        //        return BadRequest("ข้อมูลไม่ครบ");

        //    string connStr = _config["ConnectionStrings:EMS"]!;

        //    using var conn = new SqlConnection(connStr);
        //    conn.Open();

        //    using var cmd = new SqlCommand(@"
        //        UPDATE AccessTokenFacebook
        //        SET PrompSet = @promp
        //        WHERE CreateUserID = @uid AND PageID = @pid",
        //        conn);
        //    cmd.Parameters.AddWithValue("@promp", (object?)req.PrompSet ?? DBNull.Value);
        //    cmd.Parameters.AddWithValue("@uid", req.FbUserId);
        //    cmd.Parameters.AddWithValue("@pid", req.PageID);

        //    int rows = cmd.ExecuteNonQuery();
        //    return rows > 0 ? Ok("บันทึก PrompSet สำเร็จ") : NotFound("ไม่พบข้อมูล");
        //}

        //// ─────────────────────────────────────────────────────────────
        //// Helper: ดึง AccessToken ของเพจจาก DB
        //// ─────────────────────────────────────────────────────────────
        //private (string? AccessToken, string? CreateUserId) GetPageAccessToken(string pageId)
        //{
        //    try
        //    {
        //        string connStr = _config["ConnectionStrings:EMS"]!;
        //        using var conn = new SqlConnection(connStr);
        //        conn.Open();

        //        using var cmd = new SqlCommand(@"
        //            SELECT TOP 1 AccessToken, CreateUserID
        //            FROM AccessTokenFacebook
        //            WHERE PageID = @pid",
        //            conn);
        //        cmd.Parameters.AddWithValue("@pid", pageId);

        //        using var reader = cmd.ExecuteReader();
        //        if (reader.Read())
        //        {
        //            return (
        //                reader["AccessToken"].ToString(),
        //                reader["CreateUserID"].ToString()
        //            );
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        Console.WriteLine("GetPageAccessToken error: " + ex.Message);
        //    }

        //    return (null, null);// ถ้าเกิดข้อผิดพลาดหรือไม่พบข้อมูล ให้คืนค่า null ทั้งหมด
        //}

        /*
            ─────────────────────────────────────────────────────────────
            Helper: เรียก Gemini/AI แล้วส่งข้อความตอบกลับ Facebook

            senderId = ID ของ user ที่ส่งข้อความมา
            userText = ข้อความที่ user ส่งมา
            pageAccessToken = token ของเพจที่ใช้ส่งข้อความตอบกลับ
            promptSet = ข้อความ system instruction ที่ตั้งไว้สำหรับเพจนี้ (ถ้ามี)
            fbUserId = ID ของ user ที่ส่งข้อความมา (ใช้สำหรับส่งให้ AI เพื่อให้ AI รู้ว่ากำลังคุยกับใคร)
            pageId = ID ของเพจที่รับข้อความ (ใช้สำหรับส่งให้ AI เพื่อให้ AI รู้ว่ากำลังคุยกับเพจไหน เผื่อ AI จะได้ปรับคำตอบให้เหมาะสมกับแต่ละเพจได้)
            ─────────────────────────────────────────────────────────────
        */
        private async Task ProcessAndReplyAsync(string senderId, string userText, string pageAccessToken, string promptSet, string fbUserId, string pageId)
        {
            try
            {
                string aiToken = _config["AI:GeminiToken"] ?? "";
                string model = "gemini-2.5-flash";

                var geminiUrl = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={aiToken}";

                // geminiRequest คือ object ที่จะส่งไปให้ Gemini API โดยมีโครงสร้างตามที่ Gemini API ต้องการ
                var geminiRequest = new
                {
                    // ถ้า promptSet ไม่ว่าง ให้ส่ง system_instruction ไปด้วย แต่ถ้าไม่มี promptSet ก็ไม่ต้องส่ง system_instruction
                    system_instruction = string.IsNullOrEmpty(promptSet) ? null : new
                    {
                        parts = new[] { new { text = promptSet } }
                    },
                    // ข้อมูลเนื้อหาที่จะส่งให้ AI โดยในที่นี้มีแค่บทบาท "user" กับข้อความที่ user ส่งมา
                    contents = new[]
                    {
                        new { role = "user", parts = new[] { new { text = userText } } }
                    }
                };

                using var http = _httpFactory.CreateClient();
                var aiRes = await http.PostAsync(geminiUrl,
                    new StringContent(JsonConvert.SerializeObject(geminiRequest, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }),
                    Encoding.UTF8, "application/json"));

                var aiBody = await aiRes.Content.ReadAsStringAsync();
                Console.WriteLine($"Gemini response {(int)aiRes.StatusCode}: {aiBody}");

                if (!aiRes.IsSuccessStatusCode) return;

                var aiJson = JsonDocument.Parse(aiBody);
                string? replyText = aiJson.RootElement
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text")
                    .GetString();

                if (string.IsNullOrEmpty(replyText)) return;

                var fbPayload = new
                {
                    recipient = new { id = senderId },
                    message = new { text = replyText }
                };

                await http.PostAsync(
                    $"https://graph.facebook.com/v25.0/me/messages?access_token={pageAccessToken}",
                    new StringContent(JsonConvert.SerializeObject(fbPayload), Encoding.UTF8, "application/json"));
            }
            catch (Exception ex)
            {
                Console.WriteLine("ProcessAndReply error: " + ex.Message);
            }
        }

        // ─────────────────────────────────────────────────────────────
        // Model classes
        // ─────────────────────────────────────────────────────────────
        public class FacebookSessionCache
        {
            public string LongToken { get; set; } = "";
            public string PagesJson { get; set; } = "";
            public string FbUserId { get; set; } = "";
            public string FbUserName { get; set; } = "";
            public string FbUserPicture { get; set; } = "";
        }

        public class SaveSelectedPagesRequest
        {
            public string LongToken { get; set; } = "";
            public List<PageItem> Pages { get; set; } = new();
        }

        public class SavePagesRequest
        {
            public string SessionKey { get; set; } = "";
            public List<PageItem> Pages { get; set; } = new();
        }

        public class PageItem
        {
            public string PageId { get; set; } = "";
            public string PageName { get; set; } = "";
            public string PageAccessToken { get; set; } = "";
        }

        public class DisconnectPageRequest
        {
            public string FbUserId { get; set; } = "";
            public string PageID { get; set; } = "";
        }

        public class TogglePageStatusRequest
        {
            public string FbUserId   { get; set; } = "";
            public string PageID     { get; set; } = "";
            public string OpenStatus { get; set; } = "0";
        }

        public class UpdatePrompSetRequest
        {
            public string FbUserId  { get; set; } = "";
            public string PageID    { get; set; } = "";
            public string? PrompSet { get; set; }
        }
    }
}
