﻿using Microsoft.AspNetCore.Mvc;
using webchat.data;
using webchat.Models;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using MailKit.Net.Smtp;
using MimeKit;
using System;
using Microsoft.Extensions.Configuration;
using System.Text.RegularExpressions;

namespace webchat.Controllers
{
    public class LoginController : Controller
    {
        private readonly ChatDbcontect _chatDbcontect;
        private readonly IConfiguration _configuration;

        public LoginController(ChatDbcontect chatDbcontect, IConfiguration configuration)
        {
            _chatDbcontect = chatDbcontect;
            _configuration = configuration;
        }

        public IActionResult Index()
        {
            ViewData["HideNavbar"] = true;
            ViewData["HideFooter"] = true;

            var userIdCookie = Request.Cookies["UserId"];
            if (!string.IsNullOrEmpty(userIdCookie) && int.TryParse(userIdCookie, out int userId))
            {
                var user = _chatDbcontect.users.FirstOrDefault(u => u.Id == userId);
                if (user != null)
                {
                    return RedirectToAction("Index", "Home");
                }
            }
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Check(User user)
        {
            ViewData["HideNavbar"] = true;
            ViewData["HideFooter"] = true;

            if (string.IsNullOrEmpty(user.Email) || string.IsNullOrEmpty(user.PasswordHash))
            {
                ViewBag.Message = "Email and Password are required.";
                return View("Index");
            }

            var dbUser = await _chatDbcontect.users.FirstOrDefaultAsync(u => u.Email == user.Email);

            if (dbUser == null || user.PasswordHash != dbUser.PasswordHash)
            {
                ViewBag.Message = "Incorrect email or password.";
                return View("Index");
            }

            if (!Regex.IsMatch(user.PasswordHash, @"^(?=.*[A-Za-z])(?=.*\d)[A-Za-z\d]{9,}$"))
            {
                ViewBag.Message = "Password must include both letters and numbers and be at least 9 characters long.";
                return View("Index");
            }

            var cookieOptions = new CookieOptions
            {
                Expires = DateTime.Now.AddDays(7),
                HttpOnly = true
            };

            Response.Cookies.Append("UserId", dbUser.Id.ToString(), cookieOptions);
            Response.Cookies.Append("IsAdmin", dbUser.IsAdmin.ToString(), new CookieOptions
            {
                HttpOnly = true,
                Expires = DateTimeOffset.Now.AddHours(1)
            });

            await SendLoginNotificationAsync(dbUser.Email, dbUser.Username);

            return RedirectToAction("Index", "Home");
        }

        private async Task SendLoginNotificationAsync(string email, string username)
        {
            try
            {
                var deviceInfo = GetDeviceInfo();
                var loginTime = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC");

                var emailMessage = new MimeMessage();
                emailMessage.From.Add(new MailboxAddress("WebChat Security", "ChatLink.eg@gmail.com"));
                emailMessage.To.Add(new MailboxAddress(username, email));
                emailMessage.Subject = "New Login Detected";

                var body = $@"
                    <html>
                    <body>
                        <h1>Hello {username},</h1>
                        <p>We noticed a recent login to your WebChat account from the following device:</p>
                        <p><strong>Device:</strong> {deviceInfo}</p>
                        <p><strong>Time:</strong> {loginTime}</p>
                        <p>If this was you, you can ignore this message. If you don't recognize this activity, please contact us immediately.</p>
                        <p>Stay safe,<br/>The Chatlink Team</p>
                    </body>
                    </html>";

                emailMessage.Body = new TextPart("html") { Text = body };

                using (var client = new SmtpClient())
                {
                    await client.ConnectAsync("smtp.gmail.com", 587, false);
                    await client.AuthenticateAsync("ChatLink.eg@gmail.com", "ffhm glyt bcup ddke");
                    await client.SendAsync(emailMessage);
                    await client.DisconnectAsync(true);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to send login notification email: {ex.Message}");
            }
        }

        private string GetDeviceInfo()
        {
            var userAgent = Request.Headers["User-Agent"].ToString();
            var deviceInfo = "Unknown Device";

            if (userAgent.Contains("Windows"))
            {
                deviceInfo = "Windows PC";
            }
            else if (userAgent.Contains("Macintosh"))
            {
                deviceInfo = "Mac PC";
            }
            else if (userAgent.Contains("Linux"))
            {
                deviceInfo = "Linux PC";
            }
            else if (userAgent.Contains("Android"))
            {
                deviceInfo = "Android Device";
            }
            else if (userAgent.Contains("iPhone") || userAgent.Contains("iPad"))
            {
                deviceInfo = "iOS Device";
            }

            return deviceInfo;
        }

        public IActionResult ForgotPassword()
        {
            ViewData["HideNavbar"] = true;
            ViewData["HideFooter"] = true;
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> ForgotPassword(string email)
        {
            if (string.IsNullOrEmpty(email))
            {
                ViewBag.Message = "Email is required.";
                return View();
            }

            var user = await _chatDbcontect.users.FirstOrDefaultAsync(u => u.Email == email);
            if (user == null)
            {
                ViewBag.Message = "Email not registered.";
                return View();
            }

            var token = Guid.NewGuid().ToString();
            user.ResetPasswordToken = token;
            user.ResetPasswordExpiry = DateTime.Now.AddHours(1);
            await _chatDbcontect.SaveChangesAsync();

            var resetLink = Url.Action("ResetPassword", "Login", new { token, email = user.Email }, Request.Scheme);
            await SendEmailAsync(user.Email, "Reset password", $"Please click on the following link to reset your password: {resetLink}");

            return RedirectToAction("CheckEmailConfirmation");
        }

        public IActionResult CheckEmailConfirmation()
        {
            ViewData["HideNavbar"] = true;
            ViewData["HideFooter"] = true;
            return View();
        }

        public IActionResult ResetPassword(string token, string email)
        {
            ViewData["HideNavbar"] = true;
            ViewData["HideFooter"] = true;
            var user = _chatDbcontect.users.FirstOrDefault(u => u.Email == email && u.ResetPasswordToken == token && u.ResetPasswordExpiry > DateTime.Now);
            if (user == null)
            {
                ViewBag.Message = "Invalid or expired link.";
                return RedirectToAction("ForgotPassword");
            }

            ViewBag.Token = token;
            ViewBag.Email = email;
            return View();
        }

        [HttpPost]
        public IActionResult ResetPassword(string token, string email, string newPassword, string confirmPassword)
        {
            ViewData["HideNavbar"] = true;
            ViewData["HideFooter"] = true;

            if (newPassword != confirmPassword)
            {
                ViewBag.Message = "Passwords do not match.";
                return View();
            }

            var user = _chatDbcontect.users.FirstOrDefault(u => u.Email == email && u.ResetPasswordToken == token && u.ResetPasswordExpiry > DateTime.Now);
            if (user == null)
            {
                ViewBag.Message = "Invalid or expired link.";
                return RedirectToAction("ForgotPassword");
            }

            user.PasswordHash = newPassword;
            user.ResetPasswordExpiry = null;
            _chatDbcontect.SaveChanges();

            ViewBag.Message = "Password reset successfully.";
            return RedirectToAction("Index");
        }

        private async Task SendEmailAsync(string email, string subject, string message)
        {
            try
            {
                var emailMessage = new MimeMessage();
                emailMessage.From.Add(new MailboxAddress("WebChat Reset Password", "ChatLink.eg@gmail.com"));
                emailMessage.To.Add(new MailboxAddress("", email));
                emailMessage.Subject = subject;
                emailMessage.Body = new TextPart("plain") { Text = message };

                using (var client = new SmtpClient())
                {
                    await client.ConnectAsync("smtp.gmail.com", 587, false);
                    await client.AuthenticateAsync("ChatLink.eg@gmail.com", "ffhm glyt bcup ddke");
                    await client.SendAsync(emailMessage);
                    await client.DisconnectAsync(true);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to send email: {ex.Message}");
            }
        }

        public IActionResult ViewUser()
        {
            var userIdCookie = Request.Cookies["UserId"];
            ViewData["UserID"] = userIdCookie;

            if (!string.IsNullOrEmpty(userIdCookie) && int.TryParse(userIdCookie, out int userId))
            {
                var user = _chatDbcontect.users.FirstOrDefault(u => u.Id == userId);

                if (user != null)
                {
                    ViewData["time"] = user.TimeZone;
                    ViewData["nickname"] = user.NickName;
                    ViewData["Photo"] = user.ProfilePicture;
                    ViewData["Admin"] = user.IsAdmin;
                }
            }
            else
            {
                Console.WriteLine($"Invalid or missing UserId cookie: {userIdCookie}");
            }

            var isAdminCookie = Request.Cookies["IsAdmin"];
            if (string.IsNullOrEmpty(isAdminCookie) ||
                !isAdminCookie.Trim().Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                return RedirectToAction("Index", "Login");
            }

            ViewData["IsAdmin"] = isAdminCookie;

            var response = _chatDbcontect.users.ToList();
            return View(response);
        }

        public IActionResult Delete(int id)
        {
            var user = _chatDbcontect.users.Find(id);
            if (user != null)
            {
                _chatDbcontect.users.Remove(user);
                _chatDbcontect.SaveChanges();
            }
            return RedirectToAction("ViewUser", "Login");
        }
    }
}
