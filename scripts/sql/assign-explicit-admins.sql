\set ON_ERROR_STOP on
BEGIN;
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '30s';
CREATE TEMP TABLE approved_admins (email text PRIMARY KEY) ON COMMIT DROP;
INSERT INTO approved_admins SELECT lower(trim(value)) FROM jsonb_array_elements_text(:'admin_emails'::jsonb);
DO $$ BEGIN
  PERFORM i."Id" FROM "AspNetUsers" i
  JOIN "Users" u ON u."IdentityUserId" = i."Id"
  JOIN approved_admins a ON lower(u."Email") = a.email
  ORDER BY i."Id"
  FOR UPDATE OF i, u;
END $$;
DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM approved_admins) OR EXISTS (SELECT 1 FROM approved_admins WHERE email = '') THEN
    RAISE EXCEPTION 'An explicit nonempty administrator list is required';
  END IF;
  IF EXISTS (
    SELECT a.email FROM approved_admins a
    LEFT JOIN "Users" u ON lower(u."Email") = a.email AND u."IsActive"
    LEFT JOIN "AspNetUsers" i ON i."Id" = u."IdentityUserId"
    GROUP BY a.email HAVING count(i."Id") <> 1
  ) THEN
    RAISE EXCEPTION 'Each approved administrator must resolve to exactly one active identity';
  END IF;
END $$;
INSERT INTO "AspNetUserClaims" ("UserId", "ClaimType", "ClaimValue")
SELECT u."IdentityUserId", 'amftms:role', 'Admin'
FROM "Users" u JOIN approved_admins a ON lower(u."Email") = a.email
WHERE u."IsActive"
ON CONFLICT ("UserId") WHERE "ClaimType" = 'amftms:role'
DO UPDATE SET "ClaimValue" = 'Admin';
DO $$ BEGIN
  IF EXISTS (
    SELECT a.email FROM approved_admins a
    LEFT JOIN "Users" u ON lower(u."Email") = a.email AND u."IsActive"
    LEFT JOIN "AspNetUserClaims" c ON c."UserId" = u."IdentityUserId"
      AND c."ClaimType" = 'amftms:role' AND c."ClaimValue" = 'Admin'
    GROUP BY a.email HAVING count(c."Id") <> 1
  ) THEN
    RAISE EXCEPTION 'Administrator assignment postcondition failed';
  END IF;
END $$;
COMMIT;
