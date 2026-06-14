using Microsoft.AspNetCore.Mvc;

namespace CnuFacebookAPI.Controllers
{
    [Route("privacy")]
    [ApiController]
    public class PrivacyController : ControllerBase
    {
        [HttpGet]
        [Produces("text/html")]
        public ContentResult Get()
        {
            string html = """
                <!DOCTYPE html>
                <html lang="th">
                <head>
                  <meta charset="UTF-8" />
                  <meta name="viewport" content="width=device-width, initial-scale=1.0" />
                  <title>Privacy Policy — AppPage</title>
                  <style>
                    body { font-family: 'Segoe UI', Arial, sans-serif; max-width: 800px; margin: 40px auto; padding: 0 24px; color: #222; line-height: 1.7; }
                    h1 { font-size: 1.8rem; border-bottom: 2px solid #1877f2; padding-bottom: 12px; color: #1877f2; }
                    h2 { font-size: 1.2rem; margin-top: 32px; color: #333; }
                    p, li { font-size: 0.97rem; }
                    ul { padding-left: 20px; }
                    .last-updated { color: #888; font-size: 0.85rem; }
                    a { color: #1877f2; }
                  </style>
                </head>
                <body>
                  <h1>Privacy Policy</h1>
                  <p class="last-updated">Last updated: May 28, 2026</p>

                  <h2>1. Overview</h2>
                  <p>
                    AppPage ("the Application") is a business messaging automation tool that connects
                    Facebook Pages to an AI-powered auto-reply system. This Privacy Policy explains
                    what data we collect, how we use it, and how we protect it.
                  </p>

                  <h2>2. Data We Collect</h2>
                  <p>When you use the Application, we collect the following data through the Facebook Login flow:</p>
                  <ul>
                    <li><strong>Facebook Page information</strong> — Page ID and Page name of the Pages you manage</li>
                    <li><strong>Facebook Page Access Token</strong> — Used to send automated replies on your behalf</li>
                    <li><strong>Facebook User Access Token</strong> — Used temporarily to retrieve your managed Pages list; not stored permanently</li>
                    <li><strong>Messenger messages</strong> — Text messages received on your Facebook Page via webhook, used solely to generate AI responses</li>
                  </ul>

                  <h2>3. How We Use Your Data</h2>
                  <ul>
                    <li>To authenticate you with Facebook and retrieve the Pages you manage</li>
                    <li>To receive incoming messages from Facebook Messenger via webhook</li>
                    <li>To generate automated AI responses and send them back to message senders on your behalf</li>
                    <li>To allow you to enable or disable auto-reply per Page</li>
                  </ul>
                  <p>We do <strong>not</strong> sell, share, or use your data for advertising purposes.</p>

                  <h2>4. Data Retention</h2>
                  <ul>
                    <li>Page Access Tokens are stored securely in our database and used only for sending messages.</li>
                    <li>Incoming message content is processed in memory to generate a reply and is <strong>not</strong> stored persistently.</li>
                    <li>Temporary session data (Page list) is stored in server memory cache for 10 minutes only.</li>
                  </ul>

                  <h2>5. Third-Party Services</h2>
                  <p>The Application interacts with:</p>
                  <ul>
                    <li><strong>Meta (Facebook) Graph API</strong> — for authentication, Page management, and Messenger</li>
                    <li><strong>AI Generation API</strong> — to produce automated reply text; only the incoming message text and a configured persona prompt are sent</li>
                  </ul>

                  <h2>6. User Data Deletion</h2>
                  <p>
                    You may request deletion of all your data (stored Page tokens and related records) at any time
                    by contacting us at the email below. We will process your request within 30 days.
                  </p>
                  <p>
                    You may also revoke this application's access to your Facebook account at any time via
                    <a href="https://www.facebook.com/settings?tab=applications" target="_blank" rel="noopener noreferrer">
                      Facebook Settings → Apps and Websites
                    </a>.
                  </p>

                  <h2>7. Security</h2>
                  <p>
                    All data is transmitted over HTTPS. Access tokens are stored in a secured database
                    accessible only to the application server. We follow industry-standard practices to
                    protect your information.
                  </p>

                  <h2>8. Contact</h2>
                  <p>
                    If you have questions about this Privacy Policy or wish to request data deletion, contact us at:<br />
                    <a href="mailto:veerawat260546@gmail.com">veerawat260546@gmail.com</a>
                  </p>
                </body>
                </html>
                """;

            return new ContentResult
            {
                Content = html,
                ContentType = "text/html; charset=utf-8",
                StatusCode = 200
            };
        }
    }
}
