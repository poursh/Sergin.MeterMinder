-- DEV ONLY mock data: 4 regions / 12 areas, 5 manufacturers, 11 models, N devices (default 1000).
-- Run after the app has applied migrations (Development startup):
--   docker exec -i -e PGPASSWORD=<pwd> Sergin.Database psql -U postgres -d Sergin_DB -v ON_ERROR_STOP=1 -v device_count=100000 < docker-compose/.files/seed/dev-seed.sql
-- Idempotent and top-up only: scopes and catalog are created once, devices MTR-<n> are added up to device_count.
-- Never run against a real environment.
\if :{?device_count}
\else
\set device_count 1000
\endif

SELECT count(*) = 0 AS need_scopes FROM ua.scopes WHERE name = 'North Region' \gset
\if :need_scopes
BEGIN;
CREATE TEMP TABLE r(name text, areas text[]) ON COMMIT DROP;
INSERT INTO r VALUES
 ('North Region', ARRAY['Tabriz','Urmia','Ardabil']),
 ('South Region', ARRAY['Shiraz','Bushehr','Bandar Abbas']),
 ('East Region',  ARRAY['Mashhad','Birjand','Zahedan']),
 ('West Region',  ARRAY['Kermanshah','Hamadan','Ahvaz']);
INSERT INTO ua.scopes(id,name,parent_id,path,created_at_utc,created_by,row_version)
SELECT gen_random_uuid(), r.name, '01920000-0000-7000-8000-0000000000b1', ''::ltree, now(), '01920000-0000-7000-8000-00000000000f', gen_random_uuid()
FROM r;
UPDATE ua.scopes SET path = ('019200000000700080000000000000b1.' || replace(id::text,'-',''))::ltree
 WHERE parent_id='01920000-0000-7000-8000-0000000000b1' AND path=''::ltree;
INSERT INTO ua.scopes(id,name,parent_id,path,created_at_utc,created_by,row_version)
SELECT gen_random_uuid(), a, p.id, p.path, now(), '01920000-0000-7000-8000-00000000000f', gen_random_uuid()
FROM r JOIN ua.scopes p ON p.name=r.name, unnest(r.areas) a;
UPDATE ua.scopes c SET path = (p.path::text || '.' || replace(c.id::text,'-',''))::ltree
 FROM ua.scopes p WHERE c.parent_id=p.id AND p.parent_id='01920000-0000-7000-8000-0000000000b1' AND c.path=p.path;
COMMIT;
\endif

SELECT count(*) = 0 AS need_catalog FROM dm.manufacturer WHERE name = 'Iran Transfo' \gset
\if :need_catalog
BEGIN;
CREATE TEMP TABLE m(name text, addr text, models text[]) ON COMMIT DROP;
INSERT INTO m VALUES
 ('Iran Transfo','Tehran, Iran',ARRAY['ITF-1P-100','ITF-3P-200']),
 ('Landis+Gyr','Zug, Switzerland',ARRAY['E350','E450','E650']),
 ('Itron','Liberty Lake, USA',ARRAY['ACE6000','SL7000']),
 ('Kamstrup','Skanderborg, Denmark',ARRAY['OMNIPOWER','MULTICAL 603']),
 ('Elster','Essen, Germany',ARRAY['A1800','A300']);
INSERT INTO dm.manufacturer(id,name,address,created_at_utc,created_by,row_version)
SELECT gen_random_uuid(),name,addr,now(),'01920000-0000-7000-8000-00000000000f',gen_random_uuid() FROM m;
INSERT INTO dm.device_model(id,manufacturer_id,name,created_at_utc,created_by)
SELECT gen_random_uuid(), mf.id, mo, now(),'01920000-0000-7000-8000-00000000000f'
FROM m JOIN dm.manufacturer mf ON mf.name=m.name, unnest(m.models) mo;
COMMIT;
\endif

-- Devices: MTR-<n> for n in (existing mock count + 1) .. device_count, spread over the 12 leaf areas.
SELECT count(*) AS have FROM dm.device WHERE device_id LIKE 'MTR-%' \gset
SELECT :have < :device_count AS need_devices \gset
\if :need_devices
BEGIN;
WITH leaves AS (SELECT id, path, row_number() OVER (ORDER BY name) - 1 AS rn FROM ua.scopes WHERE nlevel(path)=3),
mods AS (SELECT array_agg(id ORDER BY id) AS ids FROM dm.device_model)
INSERT INTO dm.device(id,device_id,device_model_id,created_at_utc,created_by,row_version,scope_id,scope_path)
SELECT gen_random_uuid(),
  'MTR-' || CASE WHEN g < 100000 THEN lpad(g::text,5,'0') ELSE g::text END,
  mods.ids[1 + (g*7) % cardinality(mods.ids)],
  now() - (random()*interval '365 days'), '01920000-0000-7000-8000-00000000000f', gen_random_uuid(),
  l.id, l.path
FROM generate_series(:have + 1, :device_count) g
CROSS JOIN mods
JOIN leaves l ON l.rn = g % 12;
COMMIT;
\endif
ANALYZE dm.device;
SELECT count(*) AS devices FROM dm.device WHERE device_id LIKE 'MTR-%';
