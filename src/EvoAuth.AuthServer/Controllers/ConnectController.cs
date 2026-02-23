using EvoAuth.AuthServer.Identity;
using EvoAuth.Shared.Auth;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace EvoAuth.AuthServer.Controllers
{

    [ApiController]
    public class ConnectController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;

        public ConnectController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager)
        {
            _userManager = userManager;
            _signInManager = signInManager;
        }

        [HttpPost("~/connect/token")]
        public async Task<IActionResult> Exchange()
        {
            var request = HttpContext.GetOpenIddictServerRequest()
                ?? throw new InvalidOperationException("OpenIddict request not found.");

            if (request.IsPasswordGrantType())
                return await HandlePasswordGrantAsync(request);

            if (request.IsRefreshTokenGrantType())
                return await HandleRefreshGrantAsync();

            return BadRequest(new OpenIddictResponse
            {
                Error = Errors.UnsupportedGrantType,
                ErrorDescription = "Grant type is not supported."
            });
        }

        private async Task<IActionResult> HandlePasswordGrantAsync(OpenIddictRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest(new OpenIddictResponse
                {
                    Error = Errors.InvalidRequest,
                    ErrorDescription = "username/password are required."
                });
            }

            var user = await _userManager.FindByNameAsync(request.Username)
                       ?? await _userManager.FindByEmailAsync(request.Username);

            if (user is null)
            {
                return Unauthorized(new OpenIddictResponse
                {
                    Error = Errors.InvalidGrant,
                    ErrorDescription = "Invalid credentials."
                });
            }

            var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: false);
            if (!result.Succeeded)
            {
                return Unauthorized(new OpenIddictResponse
                {
                    Error = Errors.InvalidGrant,
                    ErrorDescription = "Invalid credentials."
                });
            }

            var principal = await _signInManager.CreateUserPrincipalAsync(user);


            // ✅ GARANTE o "sub" que o OpenIddict exige
            var userId = await _userManager.GetUserIdAsync(user);
            principal.SetClaim(OpenIddictConstants.Claims.Subject, userId);

            // ✅ (recomendado) username/email também
            principal.SetClaim(OpenIddictConstants.Claims.PreferredUsername, user.UserName ?? "");
            principal.SetClaim(OpenIddictConstants.Claims.Email, user.Email ?? "");

            // Scopes solicitados (ou default)
            var scopes = request.GetScopes();

            if (!scopes.Any())
                scopes.Add(AuthConstants.ApiScope);

            principal.SetScopes(scopes);

            // Associa resource ao token
            principal.SetResources(AuthConstants.ApiResource);

            // Define destinos de claims (o que vai pro access_token)
            foreach (var claim in principal.Claims)
                claim.SetDestinations(Destinations.AccessToken);

            return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        private async Task<IActionResult> HandleRefreshGrantAsync()
        {
            var result = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            if (!result.Succeeded || result.Principal is null)
            {
                return Unauthorized(new OpenIddictResponse
                {
                    Error = Errors.InvalidGrant,
                    ErrorDescription = "The refresh token is invalid."
                });
            }

            // Revalidação de usuário (opcional): aqui você pode consultar o user novamente.

            var principal = result.Principal;

            foreach (var claim in principal.Claims)
            {
                if (!claim.GetDestinations().Any())
                    claim.SetDestinations(Destinations.AccessToken);
            }

            return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }
    }
}
