using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using SquashClub.Web.Domain;

namespace SquashClub.Web.Api;

public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this IEndpointRouteBuilder routes)
    {
        var accounts = routes.MapGroup("/api/account").AddEndpointFilter<AntiforgeryFilter>();
        accounts.MapPost("/register", Register);
        accounts.MapPost("/login", Login).RequireRateLimiting("external");
        accounts.MapPost("/logout", async (SignInManager<Member> signIn) =>
        { await signIn.SignOutAsync(); return Results.Ok(); }).RequireAuthorization();
        accounts.MapGet("/confirm-email", ConfirmEmail);
        accounts.MapPost("/forgot-password", ForgotPassword).RequireRateLimiting("external");
        accounts.MapPost("/reset-password", ResetPassword).RequireRateLimiting("external");
    }

    static async Task<IResult> Register(RegisterDto dto, UserManager<Member> users,
        IHostEnvironment environment)
    {
        var member = new Member { Id = Guid.NewGuid(), UserName = dto.Email,
            Email = dto.Email, FirstName = dto.FirstName.Trim(), LastName = dto.LastName.Trim(),
            PhoneNumber = dto.PhoneNumber };
        var result = await users.CreateAsync(member, dto.Password);
        if (!result.Succeeded) return Results.ValidationProblem(result.Errors.GroupBy(x => x.Code)
            .ToDictionary(x => x.Key, x => x.Select(y => y.Description).ToArray()));
        var token = await users.GenerateEmailConfirmationTokenAsync(member);
        if (environment.IsDevelopment())
            return Results.Accepted(value: new { member.Id, confirmationToken = token });
        return Results.Accepted(value: new { member.Id });
    }

    static async Task<IResult> Login(LoginDto dto, UserManager<Member> users,
        SignInManager<Member> signIn, TimeProvider clock)
    {
        var member = await users.FindByEmailAsync(dto.Email);
        if (member is null || !member.AccountEnabled) return Results.Unauthorized();
        var result = await signIn.PasswordSignInAsync(member, dto.Password, dto.RememberMe, true);
        if (!result.Succeeded) return Results.Unauthorized();
        member.LastLoginAtUtc = clock.GetUtcNow().UtcDateTime; await users.UpdateAsync(member);
        return Results.Ok();
    }

    static async Task<IResult> ConfirmEmail(Guid memberId, string token, UserManager<Member> users)
    {
        var member = await users.FindByIdAsync(memberId.ToString());
        if (member is null) return Results.NotFound();
        var result = await users.ConfirmEmailAsync(member, token);
        return result.Succeeded ? Results.Ok() : Results.BadRequest();
    }

    static async Task<IResult> ForgotPassword(ForgotPasswordDto dto, UserManager<Member> users,
        IHostEnvironment environment)
    {
        var member = await users.FindByEmailAsync(dto.Email);
        if (member is null || !member.EmailConfirmed) return Results.Accepted();
        var token = await users.GeneratePasswordResetTokenAsync(member);
        return Results.Accepted(value: environment.IsDevelopment() ? new { resetToken = token } : null);
    }

    static async Task<IResult> ResetPassword(ResetPasswordDto dto, UserManager<Member> users)
    {
        var member = await users.FindByEmailAsync(dto.Email);
        if (member is null) return Results.BadRequest();
        var result = await users.ResetPasswordAsync(member, dto.Token, dto.NewPassword);
        return result.Succeeded ? Results.Ok() : Results.ValidationProblem(result.Errors.GroupBy(x => x.Code)
            .ToDictionary(x => x.Key, x => x.Select(y => y.Description).ToArray()));
    }
}

public sealed record RegisterDto(string FirstName, string LastName, string Email,
    string PhoneNumber, string Password);
public sealed record LoginDto(string Email, string Password, bool RememberMe);
public sealed record ForgotPasswordDto(string Email);
public sealed record ResetPasswordDto(string Email, string Token, string NewPassword);
