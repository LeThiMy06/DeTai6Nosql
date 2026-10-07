using System.Security.Claims;
using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OnlineSurvey.Models;
using OnlineSurvey.Services;

namespace OnlineSurvey.Controllers;

public sealed class AccountController : Controller
{
    private readonly IAdminRepository _adminRepository;

    public AccountController(IAdminRepository adminRepository)
    {
        _adminRepository = adminRepository;
    }

    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> Setup()
    {
        if (!IsLocalRequest())
        {
            return NotFound();
        }

        if (await _adminRepository.HasAnyAsync())
        {
            return View(new AdminSetupViewModel { IsConfigured = true });
        }

        return View(new AdminSetupViewModel());
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Setup(AdminSetupViewModel model)
    {
        if (!IsLocalRequest())
        {
            return NotFound();
        }

        if (await _adminRepository.HasAnyAsync())
        {
            return RedirectToAction(nameof(Login));
        }

        model.Username = model.Username?.Trim() ?? "";
        if (model.Username.Length < 3)
        {
            ModelState.AddModelError(nameof(model.Username), "Tên đăng nhập cần ít nhất 3 ký tự.");
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var created = await _adminRepository.CreateInitialAsync(model.Username, model.Password);
        if (!created)
        {
            return RedirectToAction(nameof(Login));
        }

        TempData["StatusMessage"] = "Đã tạo tài khoản quản trị. Hãy đăng nhập để tiếp tục.";
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> Login(string? returnUrl = null)
    {
        if (IsLocalRequest() && !await _adminRepository.HasAnyAsync())
        {
            return RedirectToAction(nameof(Setup));
        }

        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var admin = await _adminRepository.AuthenticateAsync(
            model.Username,
            model.Password);

        if (admin is null)
        {
            ModelState.AddModelError(string.Empty, "Tên đăng nhập hoặc mật khẩu không đúng.");
            return View(model);
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, admin.Id),
            new Claim(ClaimTypes.Name, admin.Username),
            new Claim(ClaimTypes.Role, admin.Role)
        };
        var identity = new ClaimsIdentity(
            claims,
            CookieAuthenticationDefaults.AuthenticationScheme);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity));

        if (!string.IsNullOrWhiteSpace(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
        {
            return Redirect(model.ReturnUrl);
        }

        return RedirectToAction("Index", "AdminSurveys");
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Index", "Home");
    }

    private bool IsLocalRequest()
    {
        var remoteIp = HttpContext.Connection.RemoteIpAddress;
        return remoteIp is not null && IPAddress.IsLoopback(remoteIp);
    }
}
