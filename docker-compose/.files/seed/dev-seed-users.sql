-- DEV ONLY mock users, roles and scoped grants. Needs dev-seed.sql first (scope tree by name).
-- Usernames match the Keycloak realm users (password = username); first sign-in links the row by username.
--   docker exec -i -e PGPASSWORD=<pwd> Sergin.Database psql -U postgres -d Sergin_DB -v ON_ERROR_STOP=1 < docker-compose/.files/seed/dev-seed-users.sql
-- Idempotent: each block runs once, guarded by a row it inserts (role region-manager, user demo.user.001).
-- A realm user who signed in first already has a row; the insert keeps it and the grants still attach to it.
SELECT count(*) = 0 AS no_tree FROM ua.scopes WHERE name = 'North Region' AND deleted_at_utc IS NULL \gset
\if :no_tree
\echo run dev-seed.sql first: the scope tree is missing
\quit
\endif
SELECT count(*) > 0 AS seeded FROM ua.roles WHERE name = 'region-manager' AND deleted_at_utc IS NULL \gset
\if :seeded
\echo realm users already seeded, skipping
\else
BEGIN;
INSERT INTO ua.users(id,user_name,is_active,email,first_name,last_name) VALUES
 (gen_random_uuid(),'admin.root',true,'admin.root@sergin.local','Root','Admin'),
 (gen_random_uuid(),'north.manager',true,'north.manager@sergin.local','North','Manager'),
 (gen_random_uuid(),'tabriz.operator',true,'tabriz.operator@sergin.local','Tabriz','Operator'),
 (gen_random_uuid(),'south.viewer',true,'south.viewer@sergin.local','South','Viewer'),
 (gen_random_uuid(),'multi.auditor',true,'multi.auditor@sergin.local','Multi','Auditor'),
 (gen_random_uuid(),'expired.user',true,'expired.user@sergin.local','Expired','User'),
 (gen_random_uuid(),'inactive.user',false,'inactive.user@sergin.local','Inactive','User')
ON CONFLICT (user_name) DO NOTHING;

INSERT INTO ua.roles(id,name,owner_scope_id,is_inheritable,row_version,created_at_utc,created_by) VALUES
 (gen_random_uuid(),'region-manager','01920000-0000-7000-8000-0000000000b1',true,gen_random_uuid(),now(),'01920000-0000-7000-8000-00000000000f'),
 (gen_random_uuid(),'operator','01920000-0000-7000-8000-0000000000b1',true,gen_random_uuid(),now(),'01920000-0000-7000-8000-00000000000f');
INSERT INTO ua.role_permissions(role_id,permission,created_at_utc,created_by)
SELECT r.id, p, now(), '01920000-0000-7000-8000-00000000000f'
FROM ua.roles r, unnest(CASE r.name
  WHEN 'region-manager' THEN ARRAY['permission.dm.devices.read','permission.dm.devices.update','permission.dm.devices.delete','permission.dm.manufacturers.read','permission.ua.users.read']
  ELSE ARRAY['permission.dm.devices.read','permission.dm.devices.update','permission.dm.manufacturers.read'] END) p
WHERE r.name IN ('region-manager','operator');

INSERT INTO ua.role_assignments(id,role_id,scope_id,include_subtree,valid_until_utc,user_id)
SELECT gen_random_uuid(), r.id, s.id, a.sub, a.until, u.id
FROM (VALUES
 ('admin.root',      'administrator',  'Root',        true,  NULL::timestamptz),
 ('north.manager',   'region-manager', 'North Region',true,  NULL),
 ('tabriz.operator', 'operator',       'Tabriz',      false, NULL),
 ('south.viewer',    'viewer',         'South Region',true,  NULL),
 ('multi.auditor',   'viewer',         'Shiraz',      false, NULL),
 ('multi.auditor',   'viewer',         'Mashhad',     false, NULL),
 ('expired.user',    'viewer',         'Root',        true,  now() - interval '7 days'),
 ('inactive.user',   'viewer',         'Root',        true,  NULL)
) a(usr,role,scope,sub,until)
JOIN ua.users u ON u.user_name=a.usr
JOIN ua.roles r ON r.name=a.role AND r.deleted_at_utc IS NULL
JOIN ua.scopes s ON s.name=a.scope AND s.deleted_at_utc IS NULL;
COMMIT;
\endif

-- Demo data for the user access pages: 40 local users (no Keycloak login) so the user list pages, roles owned
-- below the root (one not inheritable, one holding a permission no module defines, shown under "Unknown"), and
-- assignments that are scope-only, time-limited, not yet valid and revoked.
SELECT count(*) > 0 AS demo_seeded FROM ua.users WHERE user_name = 'demo.user.001' \gset
\if :demo_seeded
\echo demo users already seeded, skipping
\else
BEGIN;
INSERT INTO ua.users(id,user_name,is_active,email,first_name,last_name)
SELECT gen_random_uuid(), 'demo.user.' || lpad(g::text,3,'0'), g % 7 <> 0,
       'demo.user.' || lpad(g::text,3,'0') || '@sergin.local', 'Demo', 'User ' || lpad(g::text,3,'0')
FROM generate_series(1,40) g;

INSERT INTO ua.roles(id,name,owner_scope_id,is_inheritable,row_version,created_at_utc,created_by)
SELECT gen_random_uuid(), r.name, s.id, r.inh, gen_random_uuid(), now(), '01920000-0000-7000-8000-00000000000f'
FROM (VALUES
 ('user-admin',        'Root',         true),
 ('legacy-exporter',   'Root',         true),
 ('north-auditor',     'North Region', false),
 ('tabriz-shift-lead', 'Tabriz',       true)
) r(name,scope,inh)
JOIN ua.scopes s ON s.name=r.scope AND s.deleted_at_utc IS NULL;
INSERT INTO ua.role_permissions(role_id,permission,created_at_utc,created_by)
SELECT r.id, p, now(), '01920000-0000-7000-8000-00000000000f'
FROM ua.roles r, unnest(CASE r.name
  WHEN 'user-admin'        THEN ARRAY['permission.ua.users.read','permission.ua.users.manage','permission.ua.assignments.manage','permission.ua.roles.read','permission.ua.scopes.read']
  WHEN 'legacy-exporter'   THEN ARRAY['permission.dm.devices.read','permission.dm.devices.export']
  WHEN 'north-auditor'     THEN ARRAY['permission.dm.devices.read','permission.dm.manufacturers.read','permission.ua.users.read']
  ELSE                          ARRAY['permission.dm.devices.read','permission.dm.devices.update'] END) p
WHERE r.name IN ('user-admin','legacy-exporter','north-auditor','tabriz-shift-lead') AND r.deleted_at_utc IS NULL;

-- One assignment per demo user, its shape by n % 5; two extra rows for the two root roles.
WITH demo AS (SELECT id, substring(user_name from 11)::int AS n FROM ua.users WHERE user_name LIKE 'demo.user.%'),
leaves  AS (SELECT id, row_number() OVER (ORDER BY name) - 1 AS rn FROM ua.scopes WHERE nlevel(path)=3 AND deleted_at_utc IS NULL),
regions AS (SELECT id, row_number() OVER (ORDER BY name) - 1 AS rn FROM ua.scopes WHERE nlevel(path)=2 AND deleted_at_utc IS NULL),
plan AS (
  SELECT d.id AS user_id, d.n,
         CASE d.n % 5 WHEN 0 THEN 'viewer' WHEN 1 THEN 'operator' WHEN 2 THEN 'north-auditor'
                      WHEN 3 THEN 'tabriz-shift-lead' ELSE 'region-manager' END AS role,
         CASE d.n % 5 WHEN 0 THEN (SELECT id FROM leaves WHERE rn = d.n % 12)
                      WHEN 2 THEN (SELECT id FROM ua.scopes WHERE name='North Region' AND deleted_at_utc IS NULL)
                      WHEN 3 THEN (SELECT id FROM ua.scopes WHERE name='Tabriz' AND deleted_at_utc IS NULL)
                      ELSE (SELECT id FROM regions WHERE rn = d.n % 4) END AS scope_id
  FROM demo d)
INSERT INTO ua.role_assignments(id,role_id,scope_id,include_subtree,valid_from_utc,valid_until_utc,revoked_at_utc,revoked_by,user_id)
SELECT gen_random_uuid(), r.id, p.scope_id,
       p.n % 5 <> 0,
       CASE WHEN p.n % 5 = 3 THEN now() + interval '7 days' END,
       CASE WHEN p.n % 5 = 2 THEN now() + interval '30 days' END,
       CASE WHEN p.n % 5 = 4 THEN now() - interval '1 day' END,
       CASE WHEN p.n % 5 = 4 THEN '01920000-0000-7000-8000-00000000000f'::uuid END,
       p.user_id
FROM plan p JOIN ua.roles r ON r.name=p.role AND r.deleted_at_utc IS NULL
UNION ALL
SELECT gen_random_uuid(), r.id, '01920000-0000-7000-8000-0000000000b1', true, NULL, NULL, NULL, NULL, u.id
FROM (VALUES ('demo.user.001','user-admin'), ('demo.user.002','legacy-exporter')) x(usr,role)
JOIN ua.users u ON u.user_name=x.usr
JOIN ua.roles r ON r.name=x.role AND r.deleted_at_utc IS NULL;
COMMIT;
\endif

-- Grants for the Administration > Settings pages and the module-neutral viewer role. Runs on every pass and only
-- adds what is missing, so an already-seeded database picks it up:
--  * viewer gets the device and manufacturer reads: its seeded grant (UserAccess InitialCreate) holds only
--    UserAccess's own permission, and south.viewer / multi.auditor should still see their devices.
--  * region-manager may read languages; a root-owned translator role reads languages and edits translations,
--    and multi.auditor holds it at the root (languages belong to no scope).
BEGIN;
INSERT INTO ua.roles(id,name,owner_scope_id,is_inheritable,row_version,created_at_utc,created_by)
SELECT gen_random_uuid(),'translator','01920000-0000-7000-8000-0000000000b1',true,gen_random_uuid(),now(),'01920000-0000-7000-8000-00000000000f'
WHERE NOT EXISTS (SELECT 1 FROM ua.roles WHERE name='translator' AND deleted_at_utc IS NULL);
INSERT INTO ua.role_permissions(role_id,permission,created_at_utc,created_by)
SELECT r.id, g.permission, now(), '01920000-0000-7000-8000-00000000000f'
FROM (VALUES
 ('viewer',         'permission.dm.devices.read'),
 ('viewer',         'permission.dm.manufacturers.read'),
 ('region-manager', 'permission.st.languages.read'),
 ('translator',     'permission.st.languages.read'),
 ('translator',     'permission.st.translations.edit')
) g(role,permission)
JOIN ua.roles r ON r.name=g.role AND r.deleted_at_utc IS NULL
ON CONFLICT (role_id,permission) DO NOTHING;
INSERT INTO ua.role_assignments(id,role_id,scope_id,include_subtree,user_id)
SELECT gen_random_uuid(), r.id, '01920000-0000-7000-8000-0000000000b1', true, u.id
FROM ua.users u JOIN ua.roles r ON r.name='translator' AND r.deleted_at_utc IS NULL
WHERE u.user_name='multi.auditor'
  AND NOT EXISTS (SELECT 1 FROM ua.role_assignments a WHERE a.role_id=r.id AND a.user_id=u.id AND a.revoked_at_utc IS NULL);
COMMIT;
