using System.Diagnostics;
using DineInSystem.Models;
using Microsoft.AspNetCore.Mvc;

namespace DineInSystem.Controllers
{
    /// <summary>
    /// Home Controller - Handles the main landing page and error handling
    /// Manages user redirection based on role and displays the home page
    /// </summary>
    public class HomeController : Controller
    {

        /// <summary>
        /// Home Index - Main landing page that redirects users based on their role
        /// Admins are redirected to admin dashboard, others see the home page
        /// </summary>
        public IActionResult Index()
        {
            // Check if user is logged in (has UserId) or is a guest
            var userId = HttpContext.Session.GetString("UserId");
            var userRole = HttpContext.Session.GetString("UserRole");
            var isLoggedIn = !string.IsNullOrEmpty(userId) || userRole == "Guest";
            
            // Redirect admin users to admin dashboard
            if (userRole == "Admin")
            {
                return RedirectToAction("Index", "Admin");
            }
            
            ViewBag.IsLoggedIn = isLoggedIn;
            return View();
        }


        /// <summary>
        /// Error - Display error page for application errors
        /// </summary>
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}