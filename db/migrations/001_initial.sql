-- PostgreSQL baseline for the booking invariants. EF Core EnsureCreated supplies the
-- complete development schema; these indexes are intentionally repeated with IF NOT EXISTS.
CREATE UNIQUE INDEX IF NOT EXISTS "IX_Bookings_CourtId_StartsAtUtc_Confirmed"
  ON "Bookings" ("CourtId", "StartsAtUtc") WHERE "Status" = 0;
CREATE UNIQUE INDEX IF NOT EXISTS "IX_CreditTransactions_ExternalReference"
  ON "CreditTransactions" ("ExternalReference") WHERE "ExternalReference" IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS "IX_BookingPaymentShares_BookingId_MemberId"
  ON "BookingPaymentShares" ("BookingId", "MemberId");
CREATE UNIQUE INDEX IF NOT EXISTS "IX_LightingSessions_BookingId_Active"
  ON "LightingSessions" ("BookingId") WHERE "Status" IN (0, 1);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_LadderParticipants_LadderId_Rank"
  ON "LadderParticipants" ("LadderId", "Rank");
CREATE UNIQUE INDEX IF NOT EXISTS "IX_Payments_ProviderPaymentId"
  ON "Payments" ("ProviderPaymentId");
CREATE UNIQUE INDEX IF NOT EXISTS "IX_PaymentWebhookEvents_Provider_ProviderEventId"
  ON "PaymentWebhookEvents" ("Provider", "ProviderEventId");
CREATE UNIQUE INDEX IF NOT EXISTS "IX_LadderMatches_ChallengeId"
  ON "LadderMatches" ("ChallengeId");
-- Fixed-duration requests are also protected from arbitrary overlap at PostgreSQL level.
CREATE EXTENSION IF NOT EXISTS btree_gist;
DO $$ BEGIN
  ALTER TABLE "Bookings" ADD CONSTRAINT "EX_Bookings_NoConfirmedOverlap"
    EXCLUDE USING gist ("CourtId" WITH =, tstzrange("StartsAtUtc", "EndsAtUtc", '[)') WITH &&)
    WHERE ("Status" = 0);
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;
