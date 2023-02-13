// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using System.Linq;
using Microsoft.AspNetCore.Identity.Bearer;
using System.Text;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Exposes extensions for mapping bearer token endpoints.
/// </summary>
public static class BearerApi
{
    /// <summary>
    /// The identity endpoint prefix, "/identity"
    /// </summary>
    public const string IdentityEndpoint = $"/identity";

    /// <summary>
    /// The register users endpoint, "/identity/register";
    /// </summary>
    public const string RegisterEndpoint = $"{IdentityEndpoint}/register";

    /// <summary>
    /// The confirm email endpoint, "/identity/confirmEmail";
    /// </summary>
    public const string ConfirmEmailEndpoint = $"{IdentityEndpoint}/confirmEmail";

    /// <summary>
    /// The login endpoint, "/identity/login";
    /// </summary>
    public const string LoginEndpoint = $"{IdentityEndpoint}/login";

    /// <summary>
    /// The refresh token endpoint, "/identity/refresh";
    /// </summary>
    public const string RefreshEndpoint = $"{IdentityEndpoint}/refresh";

    /// <summary>
    /// Setup various bearer token routes under "/users".
    /// </summary>
    /// <typeparam name="TUser"></typeparam>
    /// <param name="routes"></param>
    /// <returns></returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "<Pending>")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("AOT", "IL3050:Calling members annotated with 'RequiresDynamicCodeAttribute' may break functionality when AOT compiling.", Justification = "<Pending>")]
    public static RouteGroupBuilder MapUsers<TUser>(this IEndpointRouteBuilder routes) where TUser : class, new()
    {
        var group = routes.MapGroup(IdentityEndpoint);

        group.WithTags("Users");

        // group.WithParameterValidation(typeof(UserInfo), typeof(ExternalUserInfo));

        group.MapPost("/register", async Task<Results<Ok, ValidationProblem>> (PasswordLoginInfo newUser, UserManager<TUser> userManager) =>
        {
            var user = new TUser();
            await userManager.SetUserNameAsync(user, newUser.Username);
            var result = await userManager.CreateAsync(user, newUser.Password);

            if (result.Succeeded)
            {
                return TypedResults.Ok();
            }

            return TypedResults.ValidationProblem(result.Errors.ToDictionary(e => e.Code, e => new[] { e.Description }));
        });

        group.MapPost("/login", async Task<Results<BadRequest, Ok<AuthTokens>>> (PasswordLoginInfo userInfo, UserManager<TUser> userManager, IUserTokenService<TUser> tokenService) =>
        {
            var user = await userManager.FindByNameAsync(userInfo.Username);

            if (user is null)
            {
                return TypedResults.BadRequest();
            }

            // This should go in some new SignInPolicy
            if (userManager.Options.SignIn.RequireConfirmedEmail && !(await userManager.IsEmailConfirmedAsync(user)))
            {
                return TypedResults.BadRequest();
            }
            if (userManager.Options.SignIn.RequireConfirmedPhoneNumber && !(await userManager.IsPhoneNumberConfirmedAsync(user)))
            {
                return TypedResults.BadRequest();
            }
            //if (Options.SignIn.RequireConfirmedAccount && !(await _confirmation.IsConfirmedAsync(UserManager, user)))
            //{
            //    return TypedResults.BadRequest();
            //}

            if (!await userManager.CheckPasswordAsync(user, userInfo.Password))
            {
                return TypedResults.BadRequest();
            }

            return TypedResults.Ok(new AuthTokens(await tokenService.GetAccessTokenAsync(user), await tokenService.GetRefreshTokenAsync(user)));
        });

        group.MapPost("/login/{provider}", async Task<Results<Ok<AuthTokens>, ValidationProblem>> (string provider, ExternalUserInfo userInfo, UserManager<TUser> userManager, IUserTokenService<TUser> tokenService) =>
        {
            var user = await userManager.FindByLoginAsync(provider, userInfo.ProviderKey);

            var result = IdentityResult.Success;

            if (user is null)
            {
                user = new TUser();
                await userManager.SetUserNameAsync(user, userInfo.Username);

                result = await userManager.CreateAsync(user);

                if (result.Succeeded)
                {
                    result = await userManager.AddLoginAsync(user, new UserLoginInfo(provider, userInfo.ProviderKey, displayName: null));
                }
            }

            if (result.Succeeded)
            {
                return TypedResults.Ok(new AuthTokens(await tokenService.GetAccessTokenAsync(user), await tokenService.GetRefreshTokenAsync(user)));
            }

            return TypedResults.ValidationProblem(result.Errors.ToDictionary(e => e.Code, e => new[] { e.Description }));
        });

        group.MapPost("/refresh", async Task<Results<BadRequest, Ok<AuthTokens>>> (RefreshToken tokenInfo, IUserTokenService<TUser> tokenService) =>
        {
            if (tokenInfo.Token is null)
            {
                return TypedResults.BadRequest();
            }

            (var accessToken, var refreshToken) = await tokenService.RefreshTokensAsync(tokenInfo.Token);

            if (accessToken is null || refreshToken is null)
            {
                return TypedResults.BadRequest();
            }

            return TypedResults.Ok(new AuthTokens(accessToken, refreshToken));
        });

        group.MapPost("/confirmEmail", async Task<Results<BadRequest, Ok>> (EmailConfirmation code, UserManager<TUser> userManager) =>
        {
            if (code.Token is null || code.UserId is null)
            {
                return TypedResults.BadRequest();
            }

            var user = await userManager.FindByIdAsync(code.UserId);

            if (user is null)
            {
                return TypedResults.BadRequest();
            }

            var decodedCode = Encoding.UTF8.GetString(AspNetCore.WebUtilities.WebEncoders.Base64UrlDecode(code.Token));
            var result = await userManager.ConfirmEmailAsync(user, decodedCode);

            if (result.Succeeded)
            {
                return TypedResults.Ok();
            }

            // REVIEw Should this return an error message?
            return TypedResults.BadRequest();
        });

        return group;
    }
}
