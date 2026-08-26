using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SquashClub.Web.Domain;
using SquashClub.Web.Services;

namespace SquashClub.Web.Data;

public static class DevelopmentSeed
{
    public static async Task SeedAsync(IServiceProvider root, IHostEnvironment environment)
    {
        if (!environment.IsDevelopment()) return;
        using var scope = root.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        await db.Database.EnsureCreatedAsync();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var role in new[] { "Member", "Administrator" })
            if (!await roles.RoleExistsAsync(role))
                await roles.CreateAsync(new IdentityRole<Guid>(role));

        if (!await db.Courts.AnyAsync())
        {
            var device = new LightingDevice { Id = Guid.NewGuid(), Name = "Development mock relay" };
            db.LightingDevices.Add(device);
            for (var number = 1; number <= 3; number++)
                db.Courts.Add(new Court { Id = Guid.NewGuid(), Name = $"Court {number}",
                    DisplayOrder = number, Active = true, LightingEnabled = true,
                    LightingDeviceId = device.Id });
            foreach (var day in Enum.GetValues<DayOfWeek>())
            {
                db.OpeningHours.Add(new OpeningHour { Id = Guid.NewGuid(), Day = day,
                    Opens = day == DayOfWeek.Sunday ? new TimeOnly(9, 0) : new TimeOnly(7, 0),
                    Closes = day == DayOfWeek.Sunday ? new TimeOnly(21, 0) : new TimeOnly(23, 0) });
                if (day is >= DayOfWeek.Monday and <= DayOfWeek.Friday)
                    db.PeakPeriods.Add(new PeakPeriod { Id = Guid.NewGuid(), Day = day,
                        Starts = new TimeOnly(17, 0), Ends = new TimeOnly(21, 0), CostUnits = 200 });
            }
            db.MembershipProducts.AddRange(
                new MembershipProduct { Id = Guid.NewGuid(), Name = "Adult Annual",
                    Description = "Full adult club membership", DurationDays = 365, PriceCents = 12000 },
                new MembershipProduct { Id = Guid.NewGuid(), Name = "Student Annual",
                    Description = "Student club membership", DurationDays = 365, PriceCents = 7000 },
                new MembershipProduct { Id = Guid.NewGuid(), Name = "Junior",
                    Description = "Junior club membership", DurationDays = 365, PriceCents = 5000 });
            db.CreditPackages.AddRange(
                new CreditPackage { Id = Guid.NewGuid(), Name = "5 Court Credits", Units = 500, PriceCents = 2000 },
                new CreditPackage { Id = Guid.NewGuid(), Name = "10 Court Credits", Units = 1000, PriceCents = 3500 },
                new CreditPackage { Id = Guid.NewGuid(), Name = "20 Court Credits", Units = 2000, PriceCents = 6000 });
            db.Ladders.Add(new Ladder { Id = Guid.NewGuid(), Name = "Club Ladder" });
            db.SystemSettings.AddRange(
                new SystemSetting { Key = "ClubTimezone", Value = "Europe/Dublin" },
                new SystemSetting { Key = "SlotDurationMinutes", Value = "45" },
                new SystemSetting { Key = "MaximumPeakBookingsPerMemberPerDay", Value = "1" },
                new SystemSetting { Key = "CancellationRefundCutoffMinutes", Value = "120" },
                new SystemSetting { Key = "SplitApprovalTimeoutMinutes", Value = "30" },
                new SystemSetting { Key = "LightingDurationMinutes", Value = "45" });
            await db.SaveChangesAsync();
        }

        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var seedPassword = configuration["Seed:MemberPassword"];
        if (!string.IsNullOrWhiteSpace(seedPassword))
            await SeedMembers(scope.ServiceProvider, seedPassword);
    }

    static async Task SeedMembers(IServiceProvider services, string password)
    {
        var users = services.GetRequiredService<UserManager<Member>>();
        var db = services.GetRequiredService<ClubDbContext>();
        var product = await db.MembershipProducts.FirstAsync(x => x.Name == "Adult Annual");
        foreach (var row in new[] { ("alex@club.test", "Alex", "Murphy", false),
                                    ("admin@club.test", "Club", "Administrator", true) })
        {
            if (await users.FindByEmailAsync(row.Item1) is not null) continue;
            var member = new Member { Id = Guid.NewGuid(), Email = row.Item1, UserName = row.Item1,
                EmailConfirmed = true, FirstName = row.Item2, LastName = row.Item3,
                CreditBalanceUnits = 1000 };
            var created = await users.CreateAsync(member, password);
            if (!created.Succeeded) throw new InvalidOperationException(string.Join("; ",
                created.Errors.Select(x => x.Description)));
            await users.AddToRoleAsync(member, row.Item4 ? "Administrator" : "Member");
            db.Memberships.Add(new Membership { Id = Guid.NewGuid(), MemberId = member.Id,
                ProductId = product.Id, StartsAtUtc = DateTime.UtcNow.AddDays(-1),
                EndsAtUtc = DateTime.UtcNow.AddDays(364) });
            db.CreditTransactions.Add(new CreditTransaction { Id = Guid.NewGuid(), MemberId = member.Id,
                Units = 1000, Type = CreditTransactionType.AdminAdjustment,
                Description = "Development seed allocation", ResultingBalanceUnits = 1000,
                Source = "DevelopmentSeed", CreatedAtUtc = DateTime.UtcNow });
        }
        await db.SaveChangesAsync();
    }
}

public static class ClubSettingsLoader
{
    public static async Task LoadAsync(IServiceProvider root)
    {
        using var scope = root.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        if (!await db.Database.CanConnectAsync()) return;
        var values = await db.SystemSettings.ToDictionaryAsync(x => x.Key, x => x.Value);
        var options = root.GetRequiredService<ClubOptions>();
        if (values.TryGetValue("ClubTimezone", out var timezone)) options.TimeZone = timezone;
        Set("SlotDurationMinutes", x => options.SlotMinutes = x);
        Set("OffPeakCostUnits", x => options.OffPeakCostUnits = x);
        Set("MaximumPeakBookingsPerMemberPerDay", x => options.MaximumPeakBookingsPerDay = x);
        Set("CancellationRefundCutoffMinutes", x => options.CancellationRefundCutoffMinutes = x);
        Set("SplitApprovalTimeoutMinutes", x => options.SplitTimeoutMinutes = x);
        Set("LightActivationEarlyMinutes", x => options.LightEarlyMinutes = x);
        Set("LightingDurationMinutes", x => options.LightDurationMinutes = x);
        void Set(string key, Action<int> setter)
        { if (values.TryGetValue(key, out var text) && int.TryParse(text, out var value) && value > 0) setter(value); }
    }
}
