# Live data seeder: runbook

Replaces every non-admin row (demo/test users, pages, products, orders, requests, offers, ads, notifications…) with
Arabic, Iraq-based, fictional data that looks like a live marketplace. Admin accounts, business settings (`settings` id 1),
governorates, categories, banned keywords and the audit log are kept. Missing catalog categories are added (see below).

## What gets created (defaults)

| Data | Default |
|---|---|
| Pages | 500, each with its own page user. 488 are Verified and receiving orders; a fixed handful of 12 (2.4 %) are not: 6 Pending (signed up 1–6 days ago), 3 Suspended, 3 Rejected. Those 12 are not receiving and the app hides them. |
| Products | every page gets exactly 50 (25,000 in total, unique names per page). Matches `Store:DefaultMaxProductsPerPage` = 50. |
| Customers | 50 |
| Offers | exactly 50 on customer requests |
| Store orders | ~90 + the orders behind selected offers (~105 total) |
| Ads | 50, covering every slot, owner type and status (breakdown below) |
| Wallet | every page has a balance of 25,000 IQD, and its ledger sums to exactly that (top-up first, then ad charges/refunds) |
| Followers | every Verified/Suspended page has followers (~1,000 follows); Pending/Rejected pages have none |
| Also | couriers, 8 admin-featured pages, favourites, reviews (only from delivered orders), notifications; with `Seed__SellerPassword`: seller-portal accounts and app password login for every page owner |

History is spread over the last 180 days. Phones use fictional blocks: customers `0790001xxxx`, page users `0790002xxxx`,
couriers `0790003xxxx`. Prices are in IQD.

### Showcase page

Page number 1 is a fixed demo store that is the busiest page in the data:

- **مركز الرافدين للموبايلات** (`@rafidain.mobile`), phone **07900020001**, phones/electronics, Baghdad, Verified and receiving.
- 50 products, 10 offers (6 selected), 20 store orders in every status (New, Accepted, OutForDelivery, Delivered, Cancelled,
  Rejected), the most followers, product reviews, admin-featured order 1, one running ad in each slot (home banner,
  featured page, sponsored offer) and pinned sponsored offers, balance 25,000.

At the end the seeder logs one line with the name, the phone, whether phone + password login is on (and for how many
page owners) and (only when `Seed__SellerPassword` is set) the seller-portal email `rafidain.mobile@sellers.dawwerli.test`.
The password itself is never logged.

### Logging in as the showcase owner

| Where | How |
|---|---|
| Mobile app (OTP) | `POST /api/v1/auth/otp/request {phone}` then `POST /api/v1/auth/otp/verify {phone, code, role:"page", deviceId}`. With the default `Sms:Provider = Log` **no SMS is sent and the code is not written to the logs** (only `***001`). On a test server set `Otp__FixedCode=<code>` (every OTP is that code, no SMS) or `Otp__ExposeCodeInResponse=true` (the response contains `devCode`). In production use the `Http` SMS provider (falls back to WhatsApp after 3 failed SMS). |
| Mobile app (password) | `POST /api/v1/auth/login {phone:"07900020001", password, role:"page", deviceId}`. Works when the seed ran with `Seed__SellerPassword`: **every** page owner (all 500 `0790002xxxx` phones, page *n* = `0790002` + *n* as 4 digits, e.g. page 37 = `07900020037`) gets that password, because OTP to the fictional numbers never arrives. |
| Seller portal | `POST /api/seller/v1/auth/login {email:"rafidain.mobile@sellers.dawwerli.test", password}` (every page gets `<handle>@sellers.dawwerli.test` when `Seed__SellerPassword` is set). |

### Ads (`Seed__Ads`, default 50)

The API has three slots. With the default 50:

| Slot | Owner | Statuses |
|---|---|---|
| HomeBanner (20) | Page 14 | Active 11 (1 starts later), Pending 1 (starts later), PausedBalance 1, Ended 1 |
| | Brand 3 (fictional distributors, deep link to their dealer page) | Active 3 |
| | Platform 3 ("دوّرلي") | Active 2, Ended 1 |
| FeaturedPage (16) | Page | Active 12 (1 starts later), Pending 1, Rejected 1, PausedSuspended 1, Ended 1 |
| SponsoredOffer (14) | Page | Active 10 (1 starts later), Pending 1, Rejected 1, PausedBalance 1, Ended 1 |

35 ads are running right now, from 42 different advertiser pages. Copy is Arabic, images come from the seed image pool,
prices follow the business settings (banner 5,000/day, featured 20,000/week, sponsored offer 1,500/day). Paid ads are
prepaid in the ledger like `AdsService` does it: an `AdCharge` for the booked days, a full `AdRefund` for rejected ads, a
refund of the unused days for PausedSuspended ads; PausedBalance ads were never paid (they cost more than the balance).
Seeded ads skip the minimum-rating eligibility check (seeding only; the API rules are unchanged).
Other values of `Seed__Ads` keep the same mix (fewer ads take from the front of each list, more ads repeat the plans).

Where each ad shows in the apps:

- **HomeBanner**: home screen carousel, `GET /api/v1/ads?slot=homeBanner` (and `GET /api/v1/ads/slots`), at most 10 per
  call; also the banner on the request details screen. Brand and Platform ads only exist in this slot.
- **FeaturedPage**: "featured pages" list, `GET /api/v1/pages/featured?categoryId&governorateId`, flagged `isAd` and
  sorted first. The list takes the top 20 pages, so the seeder gives featured ads to pages that are inside that top 20.
- **SponsoredOffer**: the page's first offer on a request is marked sponsored and pinned on the request
  (`requests.sponsored_offer_id`), as `OfferService` does; customers see it on top of the offers list.
  The app shows running sponsored ads to customers as the timed home popup, so every seeded sponsored ad has an image
  (first product photo, else the page photo, else a banner) and a short popup headline. Popup timing comes from the
  business settings (`PopupDurationSec`, `PopupIntervalMin`, `PopupFirstDelaySec`, `PopupMaxPerAdPerDay`).
- Sellers see their own ads at `GET /api/v1/pages/me/ads` and in the seller portal; admins at `GET /api/admin/v1/ads?status=`.

### Categories

The seeder (and the normal startup `Seeder`) adds any category from `catalog.json` whose slug is missing, keyed by slug.
Existing categories are never edited or deleted, so admin renames survive. Note: a category an admin deleted on purpose
is added again on the next startup. 13 slugs were added, 22 in total. Pages and products map to them (pages can have a
secondary category). Counts from a default run:

| Slug | Pages (incl. secondary) | Products |
|---|---|---|
| phones | 59 | 2,750 |
| shoes | 43 | 1,750 |
| clothes | 46 | 2,000 |
| perfumes | 45 | 1,400 |
| watches | 27 | 1,200 |
| care | 41 | 1,350 |
| home | 30 | 750 |
| electronics | 63 | 1,550 |
| gifts | 50 | 1,100 |
| furniture | 18 | 800 |
| appliances | 37 | 1,400 |
| gaming | 24 | 1,200 |
| car-parts | 25 | 1,200 |
| kids | 20 | 1,000 |
| sports | 12 | 600 |
| books | 17 | 850 |
| beauty | 24 | 1,200 |
| pharmacy | 8 | 400 |
| food | 19 | 750 |
| flowers | 13 | 550 |
| pets | 10 | 500 |
| tools | 18 | 700 |

### Timing and memory

Local run, 8 cores, default volume: about 6 s inside the transaction, about 8 s for the whole process. Peak memory is
about 650 MB with the default server GC and about 330 MB with `DOTNET_gcServer=0`. On a small server set
`DOTNET_gcServer=0` for the seeding run (or for the API in general).

## 1. Back up first

```bash
pg_dump -h <host> -U <user> -d <db> -Fc -f dawwerli-before-live-seed-$(date +%F-%H%M).dump
```

## 2. Option A: one-time seed on startup (recommended)

1. Set on the server: `Seed__Live=true`.
   Optional: `Seed__SellerPassword=<a strong password>` to create a seller-portal login for every page
   (`<instagram_handle>@sellers.dawwerli.test`) and the same app password (phone + password login) for every page owner.
   Without it no seller accounts are created and page owners can only use OTP.
   Optional counts: `Seed__Pages`, `Seed__ProductPages` (default 500; a value above `Seed__Pages` means every page),
   `Seed__MinProductsPerPage` / `Seed__MaxProductsPerPage` (default 50 / 50, max 200), `Seed__Customers`, `Seed__Offers`,
   `Seed__StoreOrders`, `Seed__Ads`, `Seed__FeaturedPages`, `Seed__HistoryDays`, `Seed__RandomSeed`.
2. Deploy / restart. After migrations and before the API starts listening it takes a Postgres advisory lock, checks for the
   marker row (`settings` id 9001 containing `live-seed-v2`) and, if absent, wipes and seeds in one transaction, then
   writes the marker. If anything fails the transaction rolls back and nothing changes.
3. Check the logs:
   ```
   warn: ... Live seed: Seed:Live=true and no marker live-seed-v2. DELETING all non-admin data and seeding live data now.
   warn: ... Live seed: wiped N rows: pages=…, users (non-admin)=…
   warn: ... Live seed: done in 6.2s: customers=50, page_users=500, pages=500, … Remove Seed__Live from the server environment.
   warn: ... Live seed: showcase page: مركز الرافدين للموبايلات (@rafidain.mobile), app login phone 07900020001 …
   ```
   On later restarts: `Live seed: marker live-seed-v2 found, skipping.`
4. Remove `Seed__Live` (and `Seed__SellerPassword`) from the server environment. The marker makes it safe if you forget,
   but don't leave it on. While the marker exists the QA demo scenario is never re-added.

**Upgrading from v1.** The marker key is now `live-seed-v2`. A server that already ran the first version has a
`live-seed-v1` marker, which counts as "no marker": if `Seed__Live=true` is still (or again) set, the next restart wipes
and seeds **once** with the new data, writes `live-seed-v2`, and skips from then on. If you do not want the re-seed,
make sure `Seed__Live` is not set.

To seed again later, use the CLI with `--wipe` (it ignores the marker), or delete the marker row and restart with the flag.

## 3. Option B: CLI

The command exits without starting the web host. It uses the same configuration (env vars) as the API.

```bash
dotnet Dawwerli.Api.dll seed-live --dry-run              # read only: prints host/db/user (never the password), current counts, plan
dotnet Dawwerli.Api.dll seed-live --wipe --confirm       # wipe non-admin data + seed
dotnet Dawwerli.Api.dll seed-live --wipe --confirm --seed 7 --pages 300 --customers 40 --ads 30
DOTNET_gcServer=0 dotnet Dawwerli.Api.dll seed-live --wipe --confirm   # lower memory on a small server
```

Without `--wipe` it refuses when pages or the marker already exist. Without `--confirm` it writes nothing.
In Docker: `docker compose exec api dotnet Dawwerli.Api.dll seed-live --dry-run` (use your service name).

## 4. Restore

```bash
pg_restore -h <host> -U <user> -d <db> --clean --if-exists dawwerli-before-live-seed-<stamp>.dump
```

## Notes and limits

- Uploaded media files of the deleted demo data stay in storage (only DB rows are removed).
- Page ratings come from real `ratings` rows created by the seeded offer orders, so most pages show no rating yet.
- The 12 non-verified pages still have 50 products each; the app hides them until they are verified/unsuspended.
- Only three ad slots exist (HomeBanner, FeaturedPage, SponsoredOffer); Brand and Platform ads are home banners only.
- Images are keyless Wikimedia Commons URLs, so the list below must stay with the data.

## Appendix: image attribution

Product, page and ad images are hot-linked from Wikimedia Commons under the licences below.

| Photo | Author | Licence |
|---|---|---|
| [2020 Sfigmomanometr elektroniczny.jpg](https://commons.wikimedia.org/wiki/File:2020_Sfigmomanometr_elektroniczny.jpg) | Jacek Halicki | CC BY-SA 4.0 |
| [2023 Elektroniczny termometr lekarski.jpg](https://commons.wikimedia.org/wiki/File:2023_Elektroniczny_termometr_lekarski.jpg) | Jacek Halicki | CC BY-SA 4.0 |
| [2025.11.29 Niemen Stolle Rosaline Perfume Bottle 01.jpg](https://commons.wikimedia.org/wiki/File:2025.11.29_Niemen_Stolle_Rosaline_Perfume_Bottle_01.jpg) | Illarion Kalashnikov | CC BY-SA 4.0 |
| [A bottle of BOSS ALIVE Perfume that smells sweet and of jasmine sambac.jpg](https://commons.wikimedia.org/wiki/File:A_bottle_of_BOSS_ALIVE_Perfume_that_smells_sweet_and_of_jasmine_sambac.jpg) | Sleepytimecat | CC BY 4.0 |
| [A handbag.jpg](https://commons.wikimedia.org/wiki/File:A_handbag.jpg) | ALHASSAN MOHAMED NURIDEEN | CC BY-SA 4.0 |
| [Aluminium Dumbbell.jpg](https://commons.wikimedia.org/wiki/File:Aluminium_Dumbbell.jpg) | Designproduct | CC BY-SA 4.0 |
| [Amaryllis (Hippeastrum) 18-01-2025. (actm.) 04.jpg](https://commons.wikimedia.org/wiki/File:Amaryllis_(Hippeastrum)_18-01-2025._(actm.)_04.jpg) | Agnes Monkelbaan | CC BY-SA 4.0 |
| [Analog Wrist Watch.jpg](https://commons.wikimedia.org/wiki/File:Analog_Wrist_Watch.jpg) | Aditya Oberai | CC BY-SA 4.0 |
| [B vitamin supplement tablets.jpg](https://commons.wikimedia.org/wiki/File:B_vitamin_supplement_tablets.jpg) | Ragesoss | CC BY-SA 3.0 |
| [Baby pram stroller parking foyer Silkeborg Library Denmark 02.jpg](https://commons.wikimedia.org/wiki/File:Baby_pram_stroller_parking_foyer_Silkeborg_Library_Denmark_02.jpg) | Ciara Ní Riain | CC BY-SA 4.0 |
| [Baklava - Turkish special, 80-ply.JPEG](https://commons.wikimedia.org/wiki/File:Baklava_-_Turkish_special,_80-ply.JPEG) | Kultigin | Public domain |
| [Batterie TUNDRA EFB.jpg](https://commons.wikimedia.org/wiki/File:Batterie_TUNDRA_EFB.jpg) | Rombat france | CC BY-SA 4.0 |
| [Be a natural Eyeshadow nude palette (HEMA), Hillegersberg, Rotterdam (2023) 03.jpg](https://commons.wikimedia.org/wiki/File:Be_a_natural_Eyeshadow_nude_palette_(HEMA),_Hillegersberg,_Rotterdam_(2023)_03.jpg) | Donald Trung Quoc Don (Chữ Hán: 徵國單) - Wikimedia Commons - © CC BY-SA 4.0 International.(Want to use this image?)Origina | CC BY-SA 4.0 |
| [Bedroom sadabad.jpg](https://commons.wikimedia.org/wiki/File:Bedroom_sadabad.jpg) | Fabienkhan | CC BY-SA 2.5 |
| [Blackview A60 Smartphone Android mobile phone and folio case.jpg](https://commons.wikimedia.org/wiki/File:Blackview_A60_Smartphone_Android_mobile_phone_and_folio_case.jpg) | Acabashi | CC BY-SA 4.0 |
| [Blood pressure monitoring.jpg](https://commons.wikimedia.org/wiki/File:Blood_pressure_monitoring.jpg) | rawpixel.com | CC0 |
| [Bose QuietComfort 25 Acoustic Noise Cancelling Headphones with Carry Case.jpg](https://commons.wikimedia.org/wiki/File:Bose_QuietComfort_25_Acoustic_Noise_Cancelling_Headphones_with_Carry_Case.jpg) | Florian Fuchs | CC BY-SA 3.0 |
| [Bouquet (flowers).jpg](https://commons.wikimedia.org/wiki/File:Bouquet_(flowers).jpg) | David Ring | CC0 |
| [Bowl of Dates.jpg](https://commons.wikimedia.org/wiki/File:Bowl_of_Dates.jpg) | Lebron jay | CC BY-SA 4.0 |
| [Brown gift box with red ribbon and bow.jpg](https://commons.wikimedia.org/wiki/File:Brown_gift_box_with_red_ribbon_and_bow.jpg) | Shixart1985 | CC BY 2.0 |
| [California Kitchen Microwave Oven.jpg](https://commons.wikimedia.org/wiki/File:California_Kitchen_Microwave_Oven.jpg) | Stilfehler | CC BY-SA 4.0 |
| [Car Dashcam.jpg](https://commons.wikimedia.org/wiki/File:Car_Dashcam.jpg) | Danielrayyan09 | CC BY 4.0 |
| [Car battery.jpg](https://commons.wikimedia.org/wiki/File:Car_battery.jpg) | Azorbli | CC BY-SA 4.0 |
| [Car tires.jpg](https://commons.wikimedia.org/wiki/File:Car_tires.jpg) | Angie from Sawara, Chiba-ken, Japan | CC BY 2.0 |
| [Care peach shampoo (2019) 02.jpg](https://commons.wikimedia.org/wiki/File:Care_peach_shampoo_(2019)_02.jpg) | Donald Trung Quoc Don (Chữ Hán: 徵國單) | CC BY-SA 4.0 |
| [Casio Wrist Watch 01.jpg](https://commons.wikimedia.org/wiki/File:Casio_Wrist_Watch_01.jpg) | Suyash Dwivedi | CC BY-SA 4.0 |
| [Cat food (pellets and water).jpg](https://commons.wikimedia.org/wiki/File:Cat_food_(pellets_and_water).jpg) | Deavmi | CC BY-SA 4.0 |
| [Chocolate box - Marcolini 01.jpg](https://commons.wikimedia.org/wiki/File:Chocolate_box_-_Marcolini_01.jpg) | M0tty | CC BY-SA 3.0 |
| [Claw hammer 2.jpg](https://commons.wikimedia.org/wiki/File:Claw_hammer_2.jpg) | HutheMeow | CC BY-SA 4.0 |
| [Claw hammer-1.jpg](https://commons.wikimedia.org/wiki/File:Claw_hammer-1.jpg) | Shakespeare at en.wikipedia | CC BY-SA 3.0 |
| [Clothing store interior Son Moro Cala Millor.jpg](https://commons.wikimedia.org/wiki/File:Clothing_store_interior_Son_Moro_Cala_Millor.jpg) | Steffen Mokosch | CC BY-SA 4.0 |
| [Colorful bamboo car seat cushions.jpg](https://commons.wikimedia.org/wiki/File:Colorful_bamboo_car_seat_cushions.jpg) | Fumikas Sagisavas | CC0 |
| [Cordless electric (screw) drill.jpg](https://commons.wikimedia.org/wiki/File:Cordless_electric_(screw)_drill.jpg) | Fructibus | CC0 |
| [Couch-furniture-living-room-sofa (24300293356).jpg](https://commons.wikimedia.org/wiki/File:Couch-furniture-living-room-sofa_(24300293356).jpg) | www.Pixel.la Free Stock Photos | CC0 |
| [Cube, Cyclingworld Europe 2024, Meerbusch (P1180003).jpg](https://commons.wikimedia.org/wiki/File:Cube,_Cyclingworld_Europe_2024,_Meerbusch_(P1180003).jpg) | Matti Blume | CC BY-SA 4.0 |
| [Czech Glass Perfume Bottle-1.jpg](https://commons.wikimedia.org/wiki/File:Czech_Glass_Perfume_Bottle-1.jpg) | Tangerineduel | CC BY-SA 4.0 |
| [Czech Glass Perfume Bottle-2.jpg](https://commons.wikimedia.org/wiki/File:Czech_Glass_Perfume_Bottle-2.jpg) | Tangerineduel | CC BY-SA 4.0 |
| [Dashcams P1210466.JPG](https://commons.wikimedia.org/wiki/File:Dashcams_P1210466.JPG) | Fernost | Public domain |
| [Dates fruit.jpg](https://commons.wikimedia.org/wiki/File:Dates_fruit.jpg) | Hajia Hijjah | CC BY-SA 4.0 |
| [DeadSeaCream.JPG](https://commons.wikimedia.org/wiki/File:DeadSeaCream.JPG) | Gilabrand | CC BY-SA 3.0 |
| [Denim Jacket (51079649933).jpg](https://commons.wikimedia.org/wiki/File:Denim_Jacket_(51079649933).jpg) | ajay_suresh | CC BY 2.0 |
| [Dove shampoo bottle.jpg](https://commons.wikimedia.org/wiki/File:Dove_shampoo_bottle.jpg) | Ranjima np | CC0 |
| [Emerging professions- stationery store (8347831015).jpg](https://commons.wikimedia.org/wiki/File:Emerging_professions-_stationery_store_(8347831015).jpg) | shankar s. from sharjah, united arab emirates | CC BY 2.0 |
| [Eucerin hand cream.jpg](https://commons.wikimedia.org/wiki/File:Eucerin_hand_cream.jpg) | Misaochan | CC BY-SA 3.0 |
| [Exercise Treadmill Convey Motion.jpg](https://commons.wikimedia.org/wiki/File:Exercise_Treadmill_Convey_Motion.jpg) | Larry D. Moore | CC BY 4.0 |
| [Eyeshadow Palette & Eyeshadow Brushes - 50922168141.jpg](https://commons.wikimedia.org/wiki/File:Eyeshadow_Palette_%26_Eyeshadow_Brushes_-_50922168141.jpg) | geehairimages | CC BY 2.0 |
| [Fagor washing machine front FF6314.jpg](https://commons.wikimedia.org/wiki/File:Fagor_washing_machine_front_FF6314.jpg) | Dany kg | Public domain |
| [Flower bouquet for a newborn in Christmas.jpg](https://commons.wikimedia.org/wiki/File:Flower_bouquet_for_a_newborn_in_Christmas.jpg) | M&A | CC BY-SA 4.0 |
| [Fossil wrist watch 2022.jpg](https://commons.wikimedia.org/wiki/File:Fossil_wrist_watch_2022.jpg) | Cvmontuy | CC BY-SA 4.0 |
| [Gaming computers (1).jpg](https://commons.wikimedia.org/wiki/File:Gaming_computers_(1).jpg) | Notdjey | CC BY 2.0 |
| [Giordani toy car in Fisogni Museum.jpg](https://commons.wikimedia.org/wiki/File:Giordani_toy_car_in_Fisogni_Museum.jpg) | Moxmarco | CC BY-SA 4.0 |
| [Goldfish in fish tank.jpg](https://commons.wikimedia.org/wiki/File:Goldfish_in_fish_tank.jpg) | Ashu0203 | CC BY-SA 3.0 |
| [Green shampoo bottle.jpg](https://commons.wikimedia.org/wiki/File:Green_shampoo_bottle.jpg) | Mezoneni | CC BY-SA 4.0 |
| [HITACHI HAIR DRYER HD-1650.jpg](https://commons.wikimedia.org/wiki/File:HITACHI_HAIR_DRYER_HD-1650.jpg) | Dinkun Chen | CC BY-SA 4.0 |
| [HK Ikea furniture wooden chairs seats August 2024 002.jpg](https://commons.wikimedia.org/wiki/File:HK_Ikea_furniture_wooden_chairs_seats_August_2024_002.jpg) | WuodeaJier Boa | CC0 |
| [HK Ikea furniture wooden chairs seats August 2024 003.jpg](https://commons.wikimedia.org/wiki/File:HK_Ikea_furniture_wooden_chairs_seats_August_2024_003.jpg) | WuodeaJier Boa | CC0 |
| [HYUNDAI - Air conditioner mini split (model BMS-12HD).jpg](https://commons.wikimedia.org/wiki/File:HYUNDAI_-_Air_conditioner_mini_split_(model_BMS-12HD).jpg) | AbchyZa22 | CC0 |
| [Headphones 1.jpg](https://commons.wikimedia.org/wiki/File:Headphones_1.jpg) | No machine-readable author provided. PJ assumed (based on copyright claims). | CC BY-SA 3.0 |
| [Hitachi CV-HG5 20130711.jpg](https://commons.wikimedia.org/wiki/File:Hitachi_CV-HG5_20130711.jpg) | ミトン | CC BY-SA 4.0 |
| [Huawei Smartwatch Fit 2.jpg](https://commons.wikimedia.org/wiki/File:Huawei_Smartwatch_Fit_2.jpg) | D Eaketts | CC BY-SA 4.0 |
| [IBM Thinkpad R51.jpg](https://commons.wikimedia.org/wiki/File:IBM_Thinkpad_R51.jpg) | André Karwath aka Aka | CC BY-SA 4.0 |
| [IPhone4.jpg](https://commons.wikimedia.org/wiki/File:IPhone4.jpg) | SimonWaldherr | CC BY-SA 4.0 |
| [Interior of clothing store mall.jpg](https://commons.wikimedia.org/wiki/File:Interior_of_clothing_store_mall.jpg) | BornToZucc | CC BY-SA 4.0 |
| [Jean jacket.jpg](https://commons.wikimedia.org/wiki/File:Jean_jacket.jpg) | NikosLikomitros | CC0 |
| [Jeans for men.jpg](https://commons.wikimedia.org/wiki/File:Jeans_for_men.jpg) | Honeyhuyue | Public domain |
| [Jeweler's-screwdriver-set-bright.jpg](https://commons.wikimedia.org/wiki/File:Jeweler%27s-screwdriver-set-bright.jpg) | edited by Iainf 22:19, 29 July 2006 (UTC) | Public domain |
| [Just a soccer ball (34782492153).jpg](https://commons.wikimedia.org/wiki/File:Just_a_soccer_ball_(34782492153).jpg) | bluesbby from Mountain View, USA | CC BY 2.0 |
| [Kattmat-Cat food-Katzenfutter-Nourriture pour chats - 2022.jpg](https://commons.wikimedia.org/wiki/File:Kattmat-Cat_food-Katzenfutter-Nourriture_pour_chats_-_2022.jpg) | Foto: Jonn Leffmann | CC BY 3.0 |
| [Kitchen design at a store in NJ refrigerator and cabinets 8.jpg](https://commons.wikimedia.org/wiki/File:Kitchen_design_at_a_store_in_NJ_refrigerator_and_cabinets_8.jpg) | Tomwsulcer | CC0 |
| [Kleeja 1.JPG](https://commons.wikimedia.org/wiki/File:Kleeja_1.JPG) | Abodi009 | CC BY-SA 3.0 |
| [Kleeja in Unayzah Misukaf -1.jpg](https://commons.wikimedia.org/wiki/File:Kleeja_in_Unayzah_Misukaf_-1.jpg) | Slayym | CC BY-SA 4.0 |
| [Laptop computer.jpeg](https://commons.wikimedia.org/wiki/File:Laptop_computer.jpeg) | KK IN HK | CC BY-SA 4.0 |
| [Leather boots men's.jpg](https://commons.wikimedia.org/wiki/File:Leather_boots_men%27s.jpg) | Petar Milošević | CC BY-SA 4.0 |
| [Lego bricks.jpg](https://commons.wikimedia.org/wiki/File:Lego_bricks.jpg) | Benjamin D. Esham (bdesham) | CC BY-SA 4.0 |
| [Lipstick, Cosmetics.jpg](https://commons.wikimedia.org/wiki/File:Lipstick,_Cosmetics.jpg) | Jubair1985 | CC BY-SA 4.0 |
| [Lipsticks - Κραγιόν.JPG](https://commons.wikimedia.org/wiki/File:Lipsticks_-_%CE%9A%CF%81%CE%B1%CE%B3%CE%B9%CF%8C%CE%BD.JPG) | Evi Michailidou | CC BY-SA 3.0 |
| [Marriage gift box.jpg](https://commons.wikimedia.org/wiki/File:Marriage_gift_box.jpg) | Sri2161k | CC BY-SA 4.0 |
| [Men's long-sleeve T-shirt.jpg](https://commons.wikimedia.org/wiki/File:Men%27s_long-sleeve_T-shirt.jpg) | Spreadshirt | CC BY 2.0 |
| [Microwave oven flashon.jpg](https://commons.wikimedia.org/wiki/File:Microwave_oven_flashon.jpg) | Wrightmt | Public domain |
| [Mobile phone case.jpg](https://commons.wikimedia.org/wiki/File:Mobile_phone_case.jpg) | Tiia Monto | CC BY-SA 4.0 |
| [Mobile phone shop in Malang.jpg](https://commons.wikimedia.org/wiki/File:Mobile_phone_shop_in_Malang.jpg) | Christophe95 | CC BY-SA 4.0 |
| [Mobile phone shop in South Korea 2021 (inside).jpg](https://commons.wikimedia.org/wiki/File:Mobile_phone_shop_in_South_Korea_2021_(inside).jpg) | Piotrus | CC BY-SA 4.0 |
| [Muji t-shirt compressa, dal 2001.jpg](https://commons.wikimedia.org/wiki/File:Muji_t-shirt_compressa,_dal_2001.jpg) | Sailko | CC BY 3.0 |
| [Mussel baklava with walnuts.jpg](https://commons.wikimedia.org/wiki/File:Mussel_baklava_with_walnuts.jpg) | Samizambak | CC BY-SA 4.0 |
| [Nail Polish bottles (2).jpg](https://commons.wikimedia.org/wiki/File:Nail_Polish_bottles_(2).jpg) | Joe Shlabotnik | CC BY 2.0 |
| [Nintendo Switch 2 in Docking Console.jpg](https://commons.wikimedia.org/wiki/File:Nintendo_Switch_2_in_Docking_Console.jpg) | Crisco 1492 | CC BY-SA 4.0 |
| [Nintendo-Switch-Console-Docked-wJoyConRB.jpg](https://commons.wikimedia.org/wiki/File:Nintendo-Switch-Console-Docked-wJoyConRB.jpg) | Evan-Amos | Public domain |
| [Old blender on shelf in New Orleans kitchen.jpg](https://commons.wikimedia.org/wiki/File:Old_blender_on_shelf_in_New_Orleans_kitchen.jpg) | Infrogmation of New Orleans | CC BY-SA 4.0 |
| [Paar sneakers, Nike, Air Max, objectnr 91282-1-2.JPG](https://commons.wikimedia.org/wiki/File:Paar_sneakers,_Nike,_Air_Max,_objectnr_91282-1-2.JPG) | Nike | CC BY-SA 3.0 |
| [Paar sneakers, Onitsuka, Tiger, objectnr 91283-1-2.JPG](https://commons.wikimedia.org/wiki/File:Paar_sneakers,_Onitsuka,_Tiger,_objectnr_91283-1-2.JPG) | Onitsuka | CC BY-SA 3.0 |
| [Paar witte sneakers, merk G-Unit, maat 40, objectnr 87415-7.A-B.JPG](https://commons.wikimedia.org/wiki/File:Paar_witte_sneakers,_merk_G-Unit,_maat_40,_objectnr_87415-7.A-B.JPG) | G-Unit | CC BY-SA 3.0 |
| [Packaging a teddy bear at the Vermont Teddy Bear retail store.jpg](https://commons.wikimedia.org/wiki/File:Packaging_a_teddy_bear_at_the_Vermont_Teddy_Bear_retail_store.jpg) | Tessa Bury | CC BY 4.0 |
| [Panasonic Cordless Drill & Driver EY1DD2, Ottobrunn (20250410-P1046293).jpg](https://commons.wikimedia.org/wiki/File:Panasonic_Cordless_Drill_%26_Driver_EY1DD2,_Ottobrunn_(20250410-P1046293).jpg) | Matti Blume | CC BY-SA 4.0 |
| [Panasonic HOME REFRIGERATOR NR-C320WP-N.jpg](https://commons.wikimedia.org/wiki/File:Panasonic_HOME_REFRIGERATOR_NR-C320WP-N.jpg) | Dinkun Chen | CC BY-SA 4.0 |
| [Panasonic WASHING MACHINE XQB70-H710U.jpg](https://commons.wikimedia.org/wiki/File:Panasonic_WASHING_MACHINE_XQB70-H710U.jpg) | Dinkun Chen | CC BY-SA 4.0 |
| [Perfume glass bottle.jpg](https://commons.wikimedia.org/wiki/File:Perfume_glass_bottle.jpg) | Santeri Viinamäki | CC BY-SA 4.0 |
| [Pile of light gray LEGO bricks at a LEGO store.jpg](https://commons.wikimedia.org/wiki/File:Pile_of_light_gray_LEGO_bricks_at_a_LEGO_store.jpg) | Grendelkhan | CC BY-SA 4.0 |
| [PineTime smartwatch.jpg](https://commons.wikimedia.org/wiki/File:PineTime_smartwatch.jpg) | Unnerving duck | CC0 |
| [Plaid IZOD men's shirt.jpg](https://commons.wikimedia.org/wiki/File:Plaid_IZOD_men%27s_shirt.jpg) | Punker1999 | CC0 |
| [PlayStation 5 and DualSense (2).jpg](https://commons.wikimedia.org/wiki/File:PlayStation_5_and_DualSense_(2).jpg) | Osh33m | CC BY-SA 4.0 |
| [PlayStation 5 and DualSense.jpg](https://commons.wikimedia.org/wiki/File:PlayStation_5_and_DualSense.jpg) | Osh33m | CC BY-SA 4.0 |
| [Precision Screwdriver Set 2.jpg](https://commons.wikimedia.org/wiki/File:Precision_Screwdriver_Set_2.jpg) | oomlout | CC BY-SA 2.0 |
| [Prenatal vitamin tablets.jpg](https://commons.wikimedia.org/wiki/File:Prenatal_vitamin_tablets.jpg) | Ragesoss | CC BY-SA 3.0 |
| [RGB gaming headset on desk with ambient lighting.jpg](https://commons.wikimedia.org/wiki/File:RGB_gaming_headset_on_desk_with_ambient_lighting.jpg) | SankalpSasnur | CC0 |
| [Red leather oxford shoes by Eugene Pik (2021) 01.jpg](https://commons.wikimedia.org/wiki/File:Red_leather_oxford_shoes_by_Eugene_Pik_(2021)_01.jpg) | Eugenepik | CC BY-SA 4.0 |
| [Rose and carnation flower bouquet 01.jpg](https://commons.wikimedia.org/wiki/File:Rose_and_carnation_flower_bouquet_01.jpg) | Suyash Dwivedi | CC BY-SA 4.0 |
| [Samsung Galaxy (Onyx Black) - Back.jpg](https://commons.wikimedia.org/wiki/File:Samsung_Galaxy_(Onyx_Black)_-_Back.jpg) | Beamish4 | CC BY-SA 4.0 |
| [Samsung Galaxy S20.jpg](https://commons.wikimedia.org/wiki/File:Samsung_Galaxy_S20.jpg) | Danish971 | CC BY-SA 4.0 |
| [Samsung Galaxy S6 (Gold Platinum) - Front.jpg](https://commons.wikimedia.org/wiki/File:Samsung_Galaxy_S6_(Gold_Platinum)_-_Front.jpg) | Beamish4 | CC BY-SA 4.0 |
| [Silver nail polish bottles..jpg](https://commons.wikimedia.org/wiki/File:Silver_nail_polish_bottles..jpg) | evhoffman | CC BY-SA 2.0 |
| [Smartphone Samsung Galaxy J3 2016.jpg](https://commons.wikimedia.org/wiki/File:Smartphone_Samsung_Galaxy_J3_2016.jpg) | Gzen92 | CC BY-SA 4.0 |
| [Sneakers .jpg](https://commons.wikimedia.org/wiki/File:Sneakers_.jpg) | Kid Keen 47 | CC BY-SA 4.0 |
| [Sonata wrist watch.jpg](https://commons.wikimedia.org/wiki/File:Sonata_wrist_watch.jpg) | Joydeep | CC BY-SA 3.0 |
| [Stack of Books for decor.jpg](https://commons.wikimedia.org/wiki/File:Stack_of_Books_for_decor.jpg) | mycurrency.com | CC BY-SA 4.0 |
| [Stack of multicolored books on a table.jpg](https://commons.wikimedia.org/wiki/File:Stack_of_multicolored_books_on_a_table.jpg) | Alexander Grey | CC0 |
| [Stevens, Cyclingworld Europe 2024, Meerbusch (P1170852).jpg](https://commons.wikimedia.org/wiki/File:Stevens,_Cyclingworld_Europe_2024,_Meerbusch_(P1170852).jpg) | Matti Blume | CC BY-SA 4.0 |
| [Timber Bird Cage vintage.jpg](https://commons.wikimedia.org/wiki/File:Timber_Bird_Cage_vintage.jpg) | Tangerineduel | CC BY-SA 4.0 |
| [Toolbox (49985663631).jpg](https://commons.wikimedia.org/wiki/File:Toolbox_(49985663631).jpg) | Jim Griffin | CC0 |
| [Treadmill from Viking Sport, front.jpg](https://commons.wikimedia.org/wiki/File:Treadmill_from_Viking_Sport,_front.jpg) | W.carter | CC BY 4.0 |
| [Turquoise Headphones.jpg](https://commons.wikimedia.org/wiki/File:Turquoise_Headphones.jpg) | Clarissa Savel | CC BY-SA 4.0 |
| [US Polo Wrist Watch.jpg](https://commons.wikimedia.org/wiki/File:US_Polo_Wrist_Watch.jpg) | RJ Raawat | CC BY 4.0 |
| [Visitor walking happy dog leash hoh campground camping pets d archuleta march 05 2015 (16751947833).jpg](https://commons.wikimedia.org/wiki/File:Visitor_walking_happy_dog_leash_hoh_campground_camping_pets_d_archuleta_march_05_2015_(16751947833).jpg) | Olympic National Park | Public domain |
| [Vysavač Omega 1060.jpg](https://commons.wikimedia.org/wiki/File:Vysava%C4%8D_Omega_1060.jpg) | Wartburg1973 | CC BY-SA 4.0 |
| [Wardrobe MET DP235302.jpg](https://commons.wikimedia.org/wiki/File:Wardrobe_MET_DP235302.jpg) | Herter Brothers | CC0 |
| [Wright dining table - HNT.jpg](https://commons.wikimedia.org/wiki/File:Wright_dining_table_-_HNT.jpg) | Eric Polk | CC BY-SA 4.0 |
| [Xbox Series Controller Carbon Black.jpg](https://commons.wikimedia.org/wiki/File:Xbox_Series_Controller_Carbon_Black.jpg) | uKER | CC BY-SA 4.0 |
| [Xbox-360-Wireless-Controller-White.jpg](https://commons.wikimedia.org/wiki/File:Xbox-360-Wireless-Controller-White.jpg) | Evan-Amos | Public domain |
| [Young man lifting dumbbell weight for exercise in fitness gym.jpg](https://commons.wikimedia.org/wiki/File:Young_man_lifting_dumbbell_weight_for_exercise_in_fitness_gym.jpg) | Nenad Stojkovic | CC BY 2.0 |

Icons of the new categories (bundled in `src/Dawwerli.Infrastructure/Persistence/Seed/media/<slug>.jpg`, resized), also from Wikimedia Commons:

| Icon | Author | Licence |
|---|---|---|
| furniture icon: [HK Ikea furniture wooden chairs seats August 2024 003.jpg](https://commons.wikimedia.org/wiki/File:HK_Ikea_furniture_wooden_chairs_seats_August_2024_003.jpg) | WuodeaJier Boa | CC0 |
| appliances icon: [Microwave oven flashon.jpg](https://commons.wikimedia.org/wiki/File:Microwave_oven_flashon.jpg) | Wrightmt | Public domain |
| gaming icon: [Nintendo-Switch-Console-Docked-wJoyConRB.jpg](https://commons.wikimedia.org/wiki/File:Nintendo-Switch-Console-Docked-wJoyConRB.jpg) | Evan-Amos | Public domain |
| car-parts icon: [Car tires.jpg](https://commons.wikimedia.org/wiki/File:Car_tires.jpg) | Angie from Sawara, Chiba-ken, Japan | CC BY 2.0 |
| kids icon: [Pile of light gray LEGO bricks at a LEGO store.jpg](https://commons.wikimedia.org/wiki/File:Pile_of_light_gray_LEGO_bricks_at_a_LEGO_store.jpg) | Grendelkhan | CC BY-SA 4.0 |
| sports icon: [Just a soccer ball (34782492153).jpg](https://commons.wikimedia.org/wiki/File:Just_a_soccer_ball_(34782492153).jpg) | bluesbby from Mountain View, USA | CC BY 2.0 |
| books icon: [Stack of Books for decor.jpg](https://commons.wikimedia.org/wiki/File:Stack_of_Books_for_decor.jpg) | mycurrency.com | CC BY-SA 4.0 |
| beauty icon: [Eyeshadow Palette & Eyeshadow Brushes - 50922168141.jpg](https://commons.wikimedia.org/wiki/File:Eyeshadow_Palette_%26_Eyeshadow_Brushes_-_50922168141.jpg) | geehairimages | CC BY 2.0 |
| pharmacy icon: [B vitamin supplement tablets.jpg](https://commons.wikimedia.org/wiki/File:B_vitamin_supplement_tablets.jpg) | Ragesoss | CC BY-SA 3.0 |
| food icon: [Baklava - Turkish special, 80-ply.JPEG](https://commons.wikimedia.org/wiki/File:Baklava_-_Turkish_special,_80-ply.JPEG) | Kultigin | Public domain |
| flowers icon: [Bouquet (flowers).jpg](https://commons.wikimedia.org/wiki/File:Bouquet_(flowers).jpg) | David Ring | CC0 |
| pets icon: [Kattmat-Cat food-Katzenfutter-Nourriture pour chats - 2022.jpg](https://commons.wikimedia.org/wiki/File:Kattmat-Cat_food-Katzenfutter-Nourriture_pour_chats_-_2022.jpg) | Foto: Jonn Leffmann | CC BY 3.0 |
| tools icon: [Panasonic Cordless Drill & Driver EY1DD2, Ottobrunn (20250410-P1046293).jpg](https://commons.wikimedia.org/wiki/File:Panasonic_Cordless_Drill_%26_Driver_EY1DD2,_Ottobrunn_(20250410-P1046293).jpg) | Matti Blume | CC BY-SA 4.0 |
