# Live data seeder: runbook

Replaces every non-admin row (demo/test users, pages, products, orders, requests, offers, ads, notifications…) with
Arabic, Iraq-based, fictional data that looks like a live marketplace. Admin accounts, business settings (`settings` id 1),
governorates, categories, banned keywords and the audit log are kept.

Default volume (kept small for a small server): 500 pages (all with 1 user), the first 100 pages get 10–20 products each,
50 customers, exactly 50 offers, ~90 store orders + the orders behind the offers, 1–3 couriers per product page,
8 admin-featured pages, 5 discount banners, 4 featured-page ads, follows, favourites, reviews (only from delivered orders,
one per customer per product), notifications. History is spread over the last 180 days. Phones use fictional blocks:
customers `0790001xxxx`, page users `0790002xxxx`, couriers `0790003xxxx`. Prices are in IQD.

Local run (500 pages): about 2 s inside the transaction, ~4 s for the whole process.

## 1. Back up first

```bash
pg_dump -h <host> -U <user> -d <db> -Fc -f dawwerli-before-live-seed-$(date +%F-%H%M).dump
```

## 2. Option A: one-time seed on startup (recommended)

1. Set on the server: `Seed__Live=true`.
   Optional: `Seed__SellerPassword=<a strong password>` to create a seller-portal login for each of the 100 product pages
   (`<instagram_handle>@sellers.dawwerli.test`). Without it no seller accounts are created.
   Optional counts: `Seed__Pages`, `Seed__ProductPages`, `Seed__MinProductsPerPage`, `Seed__MaxProductsPerPage`,
   `Seed__Customers`, `Seed__Offers`, `Seed__StoreOrders`, `Seed__Banners`, `Seed__FeaturedAds`, `Seed__FeaturedPages`,
   `Seed__HistoryDays`, `Seed__RandomSeed`.
2. Deploy / restart. After migrations and before the API starts listening it takes a Postgres advisory lock, checks for the
   marker row (`settings` id 9001, `live-seed-v1`) and, if absent, wipes and seeds in one transaction, then writes the marker.
   If anything fails the transaction rolls back and nothing changes.
3. Check the logs:
   ```
   warn: ... Live seed: Seed:Live=true and no marker live-seed-v1. DELETING all non-admin data and seeding live data now.
   warn: ... Live seed: wiped N rows: pages=…, users (non-admin)=…
   warn: ... Live seed: done in 1.6s: customers=50, page_users=500, pages=500, … Remove Seed__Live from the server environment.
   ```
   On later restarts: `Live seed: marker live-seed-v1 found, skipping.`
4. Remove `Seed__Live` (and `Seed__SellerPassword`) from the server environment. The marker makes it safe if you forget,
   but don't leave it on. While the marker exists the QA demo scenario is never re-added.

To seed again later, use the CLI with `--wipe` (it ignores the marker), or delete the marker row and restart with the flag.

## 3. Option B: CLI

The command exits without starting the web host. It uses the same configuration (env vars) as the API.

```bash
dotnet Dawwerli.Api.dll seed-live --dry-run              # read only: prints host/db/user (never the password), current counts, plan
dotnet Dawwerli.Api.dll seed-live --wipe --confirm       # wipe non-admin data + seed
dotnet Dawwerli.Api.dll seed-live --wipe --confirm --seed 7 --pages 300 --customers 40
```

Without `--wipe` it refuses when pages or the marker already exist. Without `--confirm` it writes nothing.
In Docker: `docker compose exec api dotnet Dawwerli.Api.dll seed-live --dry-run` (use your service name).

## 4. Restore

```bash
pg_restore -h <host> -U <user> -d <db> --clean --if-exists dawwerli-before-live-seed-<stamp>.dump
```

## Notes and limits

- Uploaded media files of the deleted demo data stay in storage (only DB rows are removed).
- Furniture/appliances use the "home" category and gaming uses "electronics" (no dedicated categories exist).
- Page ratings come from real `ratings` rows created by the seeded offer orders, so most pages show no rating yet.
- Seeded ads do not go through the ad eligibility check (minimum rating).
- Images are keyless Wikimedia Commons URLs, so the list below must stay with the data.

## Appendix: image attribution

All images are hot-linked from Wikimedia Commons under the licences below.

| Photo | Author | Licence |
|---|---|---|
| [2025.11.29 Niemen Stolle Rosaline Perfume Bottle 01.jpg](https://commons.wikimedia.org/wiki/File:2025.11.29_Niemen_Stolle_Rosaline_Perfume_Bottle_01.jpg) | Illarion Kalashnikov | CC BY-SA 4.0 |
| [A bottle of BOSS ALIVE Perfume that smells sweet and of jasmine sambac.jpg](https://commons.wikimedia.org/wiki/File:A_bottle_of_BOSS_ALIVE_Perfume_that_smells_sweet_and_of_jasmine_sambac.jpg) | Sleepytimecat | CC BY 4.0 |
| [A handbag.jpg](https://commons.wikimedia.org/wiki/File:A_handbag.jpg) | ALHASSAN MOHAMED NURIDEEN | CC BY-SA 4.0 |
| [Analog Wrist Watch.jpg](https://commons.wikimedia.org/wiki/File:Analog_Wrist_Watch.jpg) | Aditya Oberai | CC BY-SA 4.0 |
| [Blackview A60 Smartphone Android mobile phone and folio case.jpg](https://commons.wikimedia.org/wiki/File:Blackview_A60_Smartphone_Android_mobile_phone_and_folio_case.jpg) | Acabashi | CC BY-SA 4.0 |
| [Bose QuietComfort 25 Acoustic Noise Cancelling Headphones with Carry Case.jpg](https://commons.wikimedia.org/wiki/File:Bose_QuietComfort_25_Acoustic_Noise_Cancelling_Headphones_with_Carry_Case.jpg) | Florian Fuchs | CC BY-SA 3.0 |
| [Bouquet (flowers).jpg](https://commons.wikimedia.org/wiki/File:Bouquet_(flowers).jpg) | David Ring | CC0 |
| [Brown gift box with red ribbon and bow.jpg](https://commons.wikimedia.org/wiki/File:Brown_gift_box_with_red_ribbon_and_bow.jpg) | Shixart1985 | CC BY 2.0 |
| [Care peach shampoo (2019) 02.jpg](https://commons.wikimedia.org/wiki/File:Care_peach_shampoo_(2019)_02.jpg) | Donald Trung Quoc Don (Chữ Hán: 徵國單) | CC BY-SA 4.0 |
| [Casio Wrist Watch 01.jpg](https://commons.wikimedia.org/wiki/File:Casio_Wrist_Watch_01.jpg) | Suyash Dwivedi | CC BY-SA 4.0 |
| [Clothing store interior Son Moro Cala Millor.jpg](https://commons.wikimedia.org/wiki/File:Clothing_store_interior_Son_Moro_Cala_Millor.jpg) | Steffen Mokosch | CC BY-SA 4.0 |
| [Couch-furniture-living-room-sofa (24300293356).jpg](https://commons.wikimedia.org/wiki/File:Couch-furniture-living-room-sofa_(24300293356).jpg) | www.Pixel.la Free Stock Photos | CC0 |
| [Czech Glass Perfume Bottle-1.jpg](https://commons.wikimedia.org/wiki/File:Czech_Glass_Perfume_Bottle-1.jpg) | Tangerineduel | CC BY-SA 4.0 |
| [Czech Glass Perfume Bottle-2.jpg](https://commons.wikimedia.org/wiki/File:Czech_Glass_Perfume_Bottle-2.jpg) | Tangerineduel | CC BY-SA 4.0 |
| [DeadSeaCream.JPG](https://commons.wikimedia.org/wiki/File:DeadSeaCream.JPG) | Gilabrand | CC BY-SA 3.0 |
| [Denim Jacket (51079649933).jpg](https://commons.wikimedia.org/wiki/File:Denim_Jacket_(51079649933).jpg) | ajay_suresh | CC BY 2.0 |
| [Dove shampoo bottle.jpg](https://commons.wikimedia.org/wiki/File:Dove_shampoo_bottle.jpg) | Ranjima np | CC0 |
| [Eucerin hand cream.jpg](https://commons.wikimedia.org/wiki/File:Eucerin_hand_cream.jpg) | Misaochan | CC BY-SA 3.0 |
| [Fagor washing machine front FF6314.jpg](https://commons.wikimedia.org/wiki/File:Fagor_washing_machine_front_FF6314.jpg) | Dany kg | Public domain |
| [Flower bouquet for a newborn in Christmas.jpg](https://commons.wikimedia.org/wiki/File:Flower_bouquet_for_a_newborn_in_Christmas.jpg) | M&A | CC BY-SA 4.0 |
| [Fossil wrist watch 2022.jpg](https://commons.wikimedia.org/wiki/File:Fossil_wrist_watch_2022.jpg) | Cvmontuy | CC BY-SA 4.0 |
| [Green shampoo bottle.jpg](https://commons.wikimedia.org/wiki/File:Green_shampoo_bottle.jpg) | Mezoneni | CC BY-SA 4.0 |
| [HYUNDAI - Air conditioner mini split (model BMS-12HD).jpg](https://commons.wikimedia.org/wiki/File:HYUNDAI_-_Air_conditioner_mini_split_(model_BMS-12HD).jpg) | AbchyZa22 | CC0 |
| [Headphones 1.jpg](https://commons.wikimedia.org/wiki/File:Headphones_1.jpg) | No machine-readable author provided. PJ assumed (based on copyright claims). | CC BY-SA 3.0 |
| [Huawei Smartwatch Fit 2.jpg](https://commons.wikimedia.org/wiki/File:Huawei_Smartwatch_Fit_2.jpg) | D Eaketts | CC BY-SA 4.0 |
| [IBM Thinkpad R51.jpg](https://commons.wikimedia.org/wiki/File:IBM_Thinkpad_R51.jpg) | André Karwath aka Aka | CC BY-SA 4.0 |
| [IPhone4.jpg](https://commons.wikimedia.org/wiki/File:IPhone4.jpg) | SimonWaldherr | CC BY-SA 4.0 |
| [Interior of clothing store mall.jpg](https://commons.wikimedia.org/wiki/File:Interior_of_clothing_store_mall.jpg) | BornToZucc | CC BY-SA 4.0 |
| [Jean jacket.jpg](https://commons.wikimedia.org/wiki/File:Jean_jacket.jpg) | NikosLikomitros | CC0 |
| [Jeans for men.jpg](https://commons.wikimedia.org/wiki/File:Jeans_for_men.jpg) | Honeyhuyue | Public domain |
| [Kitchen design at a store in NJ refrigerator and cabinets 8.jpg](https://commons.wikimedia.org/wiki/File:Kitchen_design_at_a_store_in_NJ_refrigerator_and_cabinets_8.jpg) | Tomwsulcer | CC0 |
| [Laptop computer.jpeg](https://commons.wikimedia.org/wiki/File:Laptop_computer.jpeg) | KK IN HK | CC BY-SA 4.0 |
| [Leather boots men's.jpg](https://commons.wikimedia.org/wiki/File:Leather_boots_men%27s.jpg) | Petar Milošević | CC BY-SA 4.0 |
| [Lipstick, Cosmetics.jpg](https://commons.wikimedia.org/wiki/File:Lipstick,_Cosmetics.jpg) | Jubair1985 | CC BY-SA 4.0 |
| [Lipsticks - Κραγιόν.JPG](https://commons.wikimedia.org/wiki/File:Lipsticks_-_%CE%9A%CF%81%CE%B1%CE%B3%CE%B9%CF%8C%CE%BD.JPG) | Evi Michailidou | CC BY-SA 3.0 |
| [Marriage gift box.jpg](https://commons.wikimedia.org/wiki/File:Marriage_gift_box.jpg) | Sri2161k | CC BY-SA 4.0 |
| [Men's long-sleeve T-shirt.jpg](https://commons.wikimedia.org/wiki/File:Men%27s_long-sleeve_T-shirt.jpg) | Spreadshirt | CC BY 2.0 |
| [Mobile phone case.jpg](https://commons.wikimedia.org/wiki/File:Mobile_phone_case.jpg) | Tiia Monto | CC BY-SA 4.0 |
| [Mobile phone shop in Malang.jpg](https://commons.wikimedia.org/wiki/File:Mobile_phone_shop_in_Malang.jpg) | Christophe95 | CC BY-SA 4.0 |
| [Mobile phone shop in South Korea 2021 (inside).jpg](https://commons.wikimedia.org/wiki/File:Mobile_phone_shop_in_South_Korea_2021_(inside).jpg) | Piotrus | CC BY-SA 4.0 |
| [Muji t-shirt compressa, dal 2001.jpg](https://commons.wikimedia.org/wiki/File:Muji_t-shirt_compressa,_dal_2001.jpg) | Sailko | CC BY 3.0 |
| [Paar sneakers, Nike, Air Max, objectnr 91282-1-2.JPG](https://commons.wikimedia.org/wiki/File:Paar_sneakers,_Nike,_Air_Max,_objectnr_91282-1-2.JPG) | Nike | CC BY-SA 3.0 |
| [Paar sneakers, Onitsuka, Tiger, objectnr 91283-1-2.JPG](https://commons.wikimedia.org/wiki/File:Paar_sneakers,_Onitsuka,_Tiger,_objectnr_91283-1-2.JPG) | Onitsuka | CC BY-SA 3.0 |
| [Paar witte sneakers, merk G-Unit, maat 40, objectnr 87415-7.A-B.JPG](https://commons.wikimedia.org/wiki/File:Paar_witte_sneakers,_merk_G-Unit,_maat_40,_objectnr_87415-7.A-B.JPG) | G-Unit | CC BY-SA 3.0 |
| [Packaging a teddy bear at the Vermont Teddy Bear retail store.jpg](https://commons.wikimedia.org/wiki/File:Packaging_a_teddy_bear_at_the_Vermont_Teddy_Bear_retail_store.jpg) | Tessa Bury | CC BY 4.0 |
| [Panasonic HOME REFRIGERATOR NR-C320WP-N.jpg](https://commons.wikimedia.org/wiki/File:Panasonic_HOME_REFRIGERATOR_NR-C320WP-N.jpg) | Dinkun Chen | CC BY-SA 4.0 |
| [Panasonic WASHING MACHINE XQB70-H710U.jpg](https://commons.wikimedia.org/wiki/File:Panasonic_WASHING_MACHINE_XQB70-H710U.jpg) | Dinkun Chen | CC BY-SA 4.0 |
| [Perfume glass bottle.jpg](https://commons.wikimedia.org/wiki/File:Perfume_glass_bottle.jpg) | Santeri Viinamäki | CC BY-SA 4.0 |
| [PineTime smartwatch.jpg](https://commons.wikimedia.org/wiki/File:PineTime_smartwatch.jpg) | Unnerving duck | CC0 |
| [Plaid IZOD men's shirt.jpg](https://commons.wikimedia.org/wiki/File:Plaid_IZOD_men%27s_shirt.jpg) | Punker1999 | CC0 |
| [PlayStation 5 and DualSense (2).jpg](https://commons.wikimedia.org/wiki/File:PlayStation_5_and_DualSense_(2).jpg) | Osh33m | CC BY-SA 4.0 |
| [PlayStation 5 and DualSense.jpg](https://commons.wikimedia.org/wiki/File:PlayStation_5_and_DualSense.jpg) | Osh33m | CC BY-SA 4.0 |
| [Red leather oxford shoes by Eugene Pik (2021) 01.jpg](https://commons.wikimedia.org/wiki/File:Red_leather_oxford_shoes_by_Eugene_Pik_(2021)_01.jpg) | Eugenepik | CC BY-SA 4.0 |
| [Rose and carnation flower bouquet 01.jpg](https://commons.wikimedia.org/wiki/File:Rose_and_carnation_flower_bouquet_01.jpg) | Suyash Dwivedi | CC BY-SA 4.0 |
| [Samsung Galaxy (Onyx Black) - Back.jpg](https://commons.wikimedia.org/wiki/File:Samsung_Galaxy_(Onyx_Black)_-_Back.jpg) | Beamish4 | CC BY-SA 4.0 |
| [Samsung Galaxy S20.jpg](https://commons.wikimedia.org/wiki/File:Samsung_Galaxy_S20.jpg) | Danish971 | CC BY-SA 4.0 |
| [Samsung Galaxy S6 (Gold Platinum) - Front.jpg](https://commons.wikimedia.org/wiki/File:Samsung_Galaxy_S6_(Gold_Platinum)_-_Front.jpg) | Beamish4 | CC BY-SA 4.0 |
| [Smartphone Samsung Galaxy J3 2016.jpg](https://commons.wikimedia.org/wiki/File:Smartphone_Samsung_Galaxy_J3_2016.jpg) | Gzen92 | CC BY-SA 4.0 |
| [Sneakers .jpg](https://commons.wikimedia.org/wiki/File:Sneakers_.jpg) | Kid Keen 47 | CC BY-SA 4.0 |
| [Sonata wrist watch.jpg](https://commons.wikimedia.org/wiki/File:Sonata_wrist_watch.jpg) | Joydeep | CC BY-SA 3.0 |
| [Turquoise Headphones.jpg](https://commons.wikimedia.org/wiki/File:Turquoise_Headphones.jpg) | Clarissa Savel | CC BY-SA 4.0 |
| [US Polo Wrist Watch.jpg](https://commons.wikimedia.org/wiki/File:US_Polo_Wrist_Watch.jpg) | RJ Raawat | CC BY 4.0 |
| [Wright dining table - HNT.jpg](https://commons.wikimedia.org/wiki/File:Wright_dining_table_-_HNT.jpg) | Eric Polk | CC BY-SA 4.0 |
| [Xbox Series Controller Carbon Black.jpg](https://commons.wikimedia.org/wiki/File:Xbox_Series_Controller_Carbon_Black.jpg) | uKER | CC BY-SA 4.0 |
| [Xbox-360-Wireless-Controller-White.jpg](https://commons.wikimedia.org/wiki/File:Xbox-360-Wireless-Controller-White.jpg) | Evan-Amos | Public domain |
