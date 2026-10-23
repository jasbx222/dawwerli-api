// Freely licensed photos from Wikimedia Commons (hot-linked thumbnails, no API key). Attribution: docs/live-data-seeder.md.
namespace Dawwerli.Infrastructure.Persistence.LiveSeed;

internal static class LiveSeedImages
{
    public static readonly IReadOnlyDictionary<string, string[]> ByTag = new Dictionary<string, string[]>
    {
        ["phone"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/3/39/Samsung_Galaxy_S20.jpg/960px-Samsung_Galaxy_S20.jpg",
            "https://upload.wikimedia.org/wikipedia/commons/thumb/b/b8/Samsung_Galaxy_%28Onyx_Black%29_-_Back.jpg/960px-Samsung_Galaxy_%28Onyx_Black%29_-_Back.jpg",
            "https://upload.wikimedia.org/wikipedia/commons/thumb/f/f4/Samsung_Galaxy_S6_%28Gold_Platinum%29_-_Front.jpg/960px-Samsung_Galaxy_S6_%28Gold_Platinum%29_-_Front.jpg",
            "https://upload.wikimedia.org/wikipedia/commons/thumb/9/93/IPhone4.jpg/960px-IPhone4.jpg",
            "https://upload.wikimedia.org/wikipedia/commons/thumb/5/53/Smartphone_Samsung_Galaxy_J3_2016.jpg/960px-Smartphone_Samsung_Galaxy_J3_2016.jpg",
            "https://upload.wikimedia.org/wikipedia/commons/thumb/b/be/Blackview_A60_Smartphone_Android_mobile_phone_and_folio_case.jpg/960px-Blackview_A60_Smartphone_Android_mobile_phone_and_folio_case.jpg",
        ],
        ["phoneacc"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/6/62/Mobile_phone_case.jpg/960px-Mobile_phone_case.jpg",
            "https://upload.wikimedia.org/wikipedia/commons/thumb/b/be/Blackview_A60_Smartphone_Android_mobile_phone_and_folio_case.jpg/960px-Blackview_A60_Smartphone_Android_mobile_phone_and_folio_case.jpg",
        ],
        ["sneaker"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/f/ff/Sneakers_.jpg/960px-Sneakers_.jpg",
            "https://upload.wikimedia.org/wikipedia/commons/thumb/6/60/Paar_sneakers%2C_Nike%2C_Air_Max%2C_objectnr_91282-1-2.JPG/960px-Paar_sneakers%2C_Nike%2C_Air_Max%2C_objectnr_91282-1-2.JPG",
            "https://upload.wikimedia.org/wikipedia/commons/thumb/8/89/Paar_sneakers%2C_Onitsuka%2C_Tiger%2C_objectnr_91283-1-2.JPG/960px-Paar_sneakers%2C_Onitsuka%2C_Tiger%2C_objectnr_91283-1-2.JPG",
            "https://upload.wikimedia.org/wikipedia/commons/thumb/a/a9/Paar_witte_sneakers%2C_merk_G-Unit%2C_maat_40%2C_objectnr_87415-7.A-B.JPG/960px-Paar_witte_sneakers%2C_merk_G-Unit%2C_maat_40%2C_objectnr_87415-7.A-B.JPG",
        ],
        ["formalshoe"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/6/61/Red_leather_oxford_shoes_by_Eugene_Pik_%282021%29_01.jpg/960px-Red_leather_oxford_shoes_by_Eugene_Pik_%282021%29_01.jpg",
            "https://upload.wikimedia.org/wikipedia/commons/thumb/1/1f/Leather_boots_men%27s.jpg/960px-Leather_boots_men%27s.jpg",
        ],
        ["jacket"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/0/06/Jean_jacket.jpg/960px-Jean_jacket.jpg",
            "https://upload.wikimedia.org/wikipedia/commons/thumb/1/10/Denim_Jacket_%2851079649933%29.jpg/960px-Denim_Jacket_%2851079649933%29.jpg",
        ],
        ["jeans"] = [
            "https://upload.wikimedia.org/wikipedia/commons/d/d2/Jeans_for_men.jpg",
        ],
        ["shirt"] = [
            "https://upload.wikimedia.org/wikipedia/commons/a/ae/Plaid_IZOD_men%27s_shirt.jpg",
            "https://upload.wikimedia.org/wikipedia/commons/8/8f/Men%27s_long-sleeve_T-shirt.jpg",
            "https://upload.wikimedia.org/wikipedia/commons/thumb/b/bc/Muji_t-shirt_compressa%2C_dal_2001.jpg/960px-Muji_t-shirt_compressa%2C_dal_2001.jpg",
        ],
        ["bag"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/1/18/A_handbag.jpg/960px-A_handbag.jpg",
        ],
        ["perfume"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/7/79/Perfume_glass_bottle.jpg/960px-Perfume_glass_bottle.jpg",
            "https://upload.wikimedia.org/wikipedia/commons/thumb/4/4d/A_bottle_of_BOSS_ALIVE_Perfume_that_smells_sweet_and_of_jasmine_sambac.jpg/960px-A_bottle_of_BOSS_ALIVE_Perfume_that_smells_sweet_and_of_jasmine_sambac.jpg",
            "https://upload.wikimedia.org/wikipedia/commons/thumb/4/42/2025.11.29_Niemen_Stolle_Rosaline_Perfume_Bottle_01.jpg/960px-2025.11.29_Niemen_Stolle_Rosaline_Perfume_Bottle_01.jpg",
            "https://upload.wikimedia.org/wikipedia/commons/0/09/Czech_Glass_Perfume_Bottle-1.jpg",
            "https://upload.wikimedia.org/wikipedia/commons/thumb/a/a1/Czech_Glass_Perfume_Bottle-2.jpg/960px-Czech_Glass_Perfume_Bottle-2.jpg",
        ],
        ["watch"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/0/0c/Casio_Wrist_Watch_01.jpg/960px-Casio_Wrist_Watch_01.jpg",
            "https://upload.wikimedia.org/wikipedia/commons/thumb/8/86/Fossil_wrist_watch_2022.jpg/960px-Fossil_wrist_watch_2022.jpg",
            "https://upload.wikimedia.org/wikipedia/commons/thumb/4/4b/US_Polo_Wrist_Watch.jpg/960px-US_Polo_Wrist_Watch.jpg",
            "https://upload.wikimedia.org/wikipedia/commons/thumb/a/a8/Analog_Wrist_Watch.jpg/960px-Analog_Wrist_Watch.jpg",
            "https://upload.wikimedia.org/wikipedia/commons/thumb/7/7c/Sonata_wrist_watch.jpg/960px-Sonata_wrist_watch.jpg",
        ],
        ["smartwatch"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/f/f4/Huawei_Smartwatch_Fit_2.jpg/960px-Huawei_Smartwatch_Fit_2.jpg",
            "https://upload.wikimedia.org/wikipedia/commons/thumb/6/6c/PineTime_smartwatch.jpg/960px-PineTime_smartwatch.jpg",
        ],
        ["cream"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/3/37/Eucerin_hand_cream.jpg/960px-Eucerin_hand_cream.jpg",
            "https://upload.wikimedia.org/wikipedia/commons/thumb/0/0f/DeadSeaCream.JPG/960px-DeadSeaCream.JPG",
        ],
        ["shampoo"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/6/68/Dove_shampoo_bottle.jpg/960px-Dove_shampoo_bottle.jpg",
            "https://upload.wikimedia.org/wikipedia/commons/9/96/Green_shampoo_bottle.jpg",
            "https://upload.wikimedia.org/wikipedia/commons/thumb/a/a9/Care_peach_shampoo_%282019%29_02.jpg/960px-Care_peach_shampoo_%282019%29_02.jpg",
        ],
        ["makeup"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/e/ef/Lipstick%2C_Cosmetics.jpg/960px-Lipstick%2C_Cosmetics.jpg",
            "https://upload.wikimedia.org/wikipedia/commons/thumb/e/ed/Lipsticks_-_%CE%9A%CF%81%CE%B1%CE%B3%CE%B9%CF%8C%CE%BD.JPG/960px-Lipsticks_-_%CE%9A%CF%81%CE%B1%CE%B3%CE%B9%CF%8C%CE%BD.JPG",
        ],
        ["sofa"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/4/47/Couch-furniture-living-room-sofa_%2824300293356%29.jpg/960px-Couch-furniture-living-room-sofa_%2824300293356%29.jpg",
        ],
        ["washer"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/b/ba/Fagor_washing_machine_front_FF6314.jpg/960px-Fagor_washing_machine_front_FF6314.jpg",
            "https://upload.wikimedia.org/wikipedia/commons/thumb/0/09/Panasonic_WASHING_MACHINE_XQB70-H710U.jpg/960px-Panasonic_WASHING_MACHINE_XQB70-H710U.jpg",
        ],
        ["fridge"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/6/61/Panasonic_HOME_REFRIGERATOR_NR-C320WP-N.jpg/960px-Panasonic_HOME_REFRIGERATOR_NR-C320WP-N.jpg",
            "https://upload.wikimedia.org/wikipedia/commons/3/34/Kitchen_design_at_a_store_in_NJ_refrigerator_and_cabinets_8.jpg",
        ],
        ["ac"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/1/16/HYUNDAI_-_Air_conditioner_mini_split_%28model_BMS-12HD%29.jpg/960px-HYUNDAI_-_Air_conditioner_mini_split_%28model_BMS-12HD%29.jpg",
        ],
        ["table"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/2/28/Wright_dining_table_-_HNT.jpg/960px-Wright_dining_table_-_HNT.jpg",
        ],
        ["laptop"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/b/bd/Laptop_computer.jpeg/960px-Laptop_computer.jpeg",
            "https://upload.wikimedia.org/wikipedia/commons/thumb/0/0e/IBM_Thinkpad_R51.jpg/960px-IBM_Thinkpad_R51.jpg",
        ],
        ["headphones"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/0/0a/Bose_QuietComfort_25_Acoustic_Noise_Cancelling_Headphones_with_Carry_Case.jpg/960px-Bose_QuietComfort_25_Acoustic_Noise_Cancelling_Headphones_with_Carry_Case.jpg",
            "https://upload.wikimedia.org/wikipedia/commons/thumb/4/40/Headphones_1.jpg/960px-Headphones_1.jpg",
            "https://upload.wikimedia.org/wikipedia/commons/8/8e/Turquoise_Headphones.jpg",
        ],
        ["console"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/0/00/PlayStation_5_and_DualSense.jpg/960px-PlayStation_5_and_DualSense.jpg",
            "https://upload.wikimedia.org/wikipedia/commons/thumb/f/f4/PlayStation_5_and_DualSense_%282%29.jpg/960px-PlayStation_5_and_DualSense_%282%29.jpg",
        ],
        ["controller"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/c/cb/Xbox_Series_Controller_Carbon_Black.jpg/960px-Xbox_Series_Controller_Carbon_Black.jpg",
            "https://upload.wikimedia.org/wikipedia/commons/thumb/4/4d/Xbox-360-Wireless-Controller-White.jpg/960px-Xbox-360-Wireless-Controller-White.jpg",
        ],
        ["giftbox"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/8/8f/Brown_gift_box_with_red_ribbon_and_bow.jpg/960px-Brown_gift_box_with_red_ribbon_and_bow.jpg",
            "https://upload.wikimedia.org/wikipedia/commons/thumb/6/61/Marriage_gift_box.jpg/960px-Marriage_gift_box.jpg",
        ],
        ["flowers"] = [
            "https://upload.wikimedia.org/wikipedia/commons/a/ae/Bouquet_%28flowers%29.jpg",
            "https://upload.wikimedia.org/wikipedia/commons/thumb/4/44/Rose_and_carnation_flower_bouquet_01.jpg/960px-Rose_and_carnation_flower_bouquet_01.jpg",
            "https://upload.wikimedia.org/wikipedia/commons/thumb/1/19/Flower_bouquet_for_a_newborn_in_Christmas.jpg/960px-Flower_bouquet_for_a_newborn_in_Christmas.jpg",
        ],
        ["teddy"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/5/54/Packaging_a_teddy_bear_at_the_Vermont_Teddy_Bear_retail_store.jpg/960px-Packaging_a_teddy_bear_at_the_Vermont_Teddy_Bear_retail_store.jpg",
        ],
        ["banner"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/9/99/Interior_of_clothing_store_mall.jpg/960px-Interior_of_clothing_store_mall.jpg",
            "https://upload.wikimedia.org/wikipedia/commons/thumb/d/dc/Mobile_phone_shop_in_Malang.jpg/960px-Mobile_phone_shop_in_Malang.jpg",
            "https://upload.wikimedia.org/wikipedia/commons/thumb/f/f7/Clothing_store_interior_Son_Moro_Cala_Millor.jpg/960px-Clothing_store_interior_Son_Moro_Cala_Millor.jpg",
            "https://upload.wikimedia.org/wikipedia/commons/thumb/9/9e/Mobile_phone_shop_in_South_Korea_2021_%28inside%29.jpg/960px-Mobile_phone_shop_in_South_Korea_2021_%28inside%29.jpg",
        ],
        ["bed"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/0/0b/Bedroom_sadabad.jpg/960px-Bedroom_sadabad.jpg",
        ],
        ["chair"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/c/c0/HK_Ikea_furniture_wooden_chairs_seats_August_2024_003.jpg/960px-HK_Ikea_furniture_wooden_chairs_seats_August_2024_003.jpg",
            "https://upload.wikimedia.org/wikipedia/commons/thumb/9/93/HK_Ikea_furniture_wooden_chairs_seats_August_2024_002.jpg/960px-HK_Ikea_furniture_wooden_chairs_seats_August_2024_002.jpg",
        ],
        ["wardrobe"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/5/5f/Wardrobe_MET_DP235302.jpg/960px-Wardrobe_MET_DP235302.jpg",
        ],
        ["microwave"] = [
            "https://upload.wikimedia.org/wikipedia/commons/1/12/Microwave_oven_flashon.jpg",
            "https://upload.wikimedia.org/wikipedia/commons/thumb/f/f9/California_Kitchen_Microwave_Oven.jpg/960px-California_Kitchen_Microwave_Oven.jpg",
        ],
        ["vacuum"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/f/fc/Hitachi_CV-HG5_20130711.jpg/960px-Hitachi_CV-HG5_20130711.jpg",
            "https://upload.wikimedia.org/wikipedia/commons/6/62/Vysava%C4%8D_Omega_1060.jpg",
        ],
        ["blender"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/6/6d/Old_blender_on_shelf_in_New_Orleans_kitchen.jpg/960px-Old_blender_on_shelf_in_New_Orleans_kitchen.jpg",
        ],
        ["gamepad"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/7/76/Nintendo-Switch-Console-Docked-wJoyConRB.jpg/960px-Nintendo-Switch-Console-Docked-wJoyConRB.jpg",
            "https://upload.wikimedia.org/wikipedia/commons/thumb/4/4e/Nintendo_Switch_2_in_Docking_Console.jpg/960px-Nintendo_Switch_2_in_Docking_Console.jpg",
        ],
        ["gamingheadset"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/7/7a/RGB_gaming_headset_on_desk_with_ambient_lighting.jpg/960px-RGB_gaming_headset_on_desk_with_ambient_lighting.jpg",
            "https://upload.wikimedia.org/wikipedia/commons/thumb/7/78/Gaming_computers_%281%29.jpg/960px-Gaming_computers_%281%29.jpg",
        ],
        ["tire"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/1/14/Car_tires.jpg/960px-Car_tires.jpg",
        ],
        ["carbattery"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/4/4c/Car_battery.jpg/960px-Car_battery.jpg",
            "https://upload.wikimedia.org/wikipedia/commons/0/04/Batterie_TUNDRA_EFB.jpg",
        ],
        ["dashcam"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/4/43/Dashcams_P1210466.JPG/960px-Dashcams_P1210466.JPG",
            "https://upload.wikimedia.org/wikipedia/commons/thumb/b/b9/Car_Dashcam.jpg/960px-Car_Dashcam.jpg",
        ],
        ["carseat"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/7/70/Colorful_bamboo_car_seat_cushions.jpg/960px-Colorful_bamboo_car_seat_cushions.jpg",
        ],
        ["lego"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/4/45/Pile_of_light_gray_LEGO_bricks_at_a_LEGO_store.jpg/960px-Pile_of_light_gray_LEGO_bricks_at_a_LEGO_store.jpg",
            "https://upload.wikimedia.org/wikipedia/commons/thumb/1/19/Lego_bricks.jpg/960px-Lego_bricks.jpg",
        ],
        ["stroller"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/9/99/Baby_pram_stroller_parking_foyer_Silkeborg_Library_Denmark_02.jpg/960px-Baby_pram_stroller_parking_foyer_Silkeborg_Library_Denmark_02.jpg",
        ],
        ["toycar"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/c/c3/Giordani_toy_car_in_Fisogni_Museum.jpg/960px-Giordani_toy_car_in_Fisogni_Museum.jpg",
        ],
        ["football"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/3/3d/Just_a_soccer_ball_%2834782492153%29.jpg/960px-Just_a_soccer_ball_%2834782492153%29.jpg",
        ],
        ["dumbbell"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/4/40/Aluminium_Dumbbell.jpg/960px-Aluminium_Dumbbell.jpg",
            "https://upload.wikimedia.org/wikipedia/commons/thumb/c/c1/Young_man_lifting_dumbbell_weight_for_exercise_in_fitness_gym.jpg/960px-Young_man_lifting_dumbbell_weight_for_exercise_in_fitness_gym.jpg",
        ],
        ["bicycle"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/1/19/Cube%2C_Cyclingworld_Europe_2024%2C_Meerbusch_%28P1180003%29.jpg/960px-Cube%2C_Cyclingworld_Europe_2024%2C_Meerbusch_%28P1180003%29.jpg",
            "https://upload.wikimedia.org/wikipedia/commons/thumb/9/9e/Stevens%2C_Cyclingworld_Europe_2024%2C_Meerbusch_%28P1170852%29.jpg/960px-Stevens%2C_Cyclingworld_Europe_2024%2C_Meerbusch_%28P1170852%29.jpg",
        ],
        ["treadmill"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/1/1f/Treadmill_from_Viking_Sport%2C_front.jpg/960px-Treadmill_from_Viking_Sport%2C_front.jpg",
            "https://upload.wikimedia.org/wikipedia/commons/thumb/9/95/Exercise_Treadmill_Convey_Motion.jpg/960px-Exercise_Treadmill_Convey_Motion.jpg",
        ],
        ["books"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/3/32/Stack_of_Books_for_decor.jpg/960px-Stack_of_Books_for_decor.jpg",
            "https://upload.wikimedia.org/wikipedia/commons/thumb/2/2b/Stack_of_multicolored_books_on_a_table.jpg/960px-Stack_of_multicolored_books_on_a_table.jpg",
        ],
        ["stationery"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/e/e2/Emerging_professions-_stationery_store_%288347831015%29.jpg/960px-Emerging_professions-_stationery_store_%288347831015%29.jpg",
        ],
        ["nailpolish"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/3/37/Silver_nail_polish_bottles..jpg/960px-Silver_nail_polish_bottles..jpg",
            "https://upload.wikimedia.org/wikipedia/commons/thumb/9/97/Nail_Polish_bottles_%282%29.jpg/960px-Nail_Polish_bottles_%282%29.jpg",
        ],
        ["hairdryer"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/1/1f/HITACHI_HAIR_DRYER_HD-1650.jpg/960px-HITACHI_HAIR_DRYER_HD-1650.jpg",
        ],
        ["eyeshadow"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/6/62/Eyeshadow_Palette_%26_Eyeshadow_Brushes_-_50922168141.jpg/960px-Eyeshadow_Palette_%26_Eyeshadow_Brushes_-_50922168141.jpg",
            "https://upload.wikimedia.org/wikipedia/commons/thumb/9/95/Be_a_natural_Eyeshadow_nude_palette_%28HEMA%29%2C_Hillegersberg%2C_Rotterdam_%282023%29_03.jpg/960px-Be_a_natural_Eyeshadow_nude_palette_%28HEMA%29%2C_Hillegersberg%2C_Rotterdam_%282023%29_03.jpg",
        ],
        ["vitamins"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/6/66/B_vitamin_supplement_tablets.jpg/960px-B_vitamin_supplement_tablets.jpg",
            "https://upload.wikimedia.org/wikipedia/commons/thumb/4/42/Prenatal_vitamin_tablets.jpg/960px-Prenatal_vitamin_tablets.jpg",
        ],
        ["bpmonitor"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/4/41/2020_Sfigmomanometr_elektroniczny.jpg/960px-2020_Sfigmomanometr_elektroniczny.jpg",
            "https://upload.wikimedia.org/wikipedia/commons/thumb/c/c6/Blood_pressure_monitoring.jpg/960px-Blood_pressure_monitoring.jpg",
        ],
        ["thermometer"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/1/16/2023_Elektroniczny_termometr_lekarski.jpg/960px-2023_Elektroniczny_termometr_lekarski.jpg",
        ],
        ["baklava"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/8/8f/Baklava_-_Turkish_special%2C_80-ply.JPEG/960px-Baklava_-_Turkish_special%2C_80-ply.JPEG",
            "https://upload.wikimedia.org/wikipedia/commons/thumb/b/b8/Mussel_baklava_with_walnuts.jpg/960px-Mussel_baklava_with_walnuts.jpg",
        ],
        ["dates"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/1/15/Dates_fruit.jpg/960px-Dates_fruit.jpg",
            "https://upload.wikimedia.org/wikipedia/commons/thumb/8/8a/Bowl_of_Dates.jpg/960px-Bowl_of_Dates.jpg",
        ],
        ["kleicha"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/3/3b/Kleeja_1.JPG/960px-Kleeja_1.JPG",
            "https://upload.wikimedia.org/wikipedia/commons/thumb/6/63/Kleeja_in_Unayzah_Misukaf_-1.jpg/960px-Kleeja_in_Unayzah_Misukaf_-1.jpg",
        ],
        ["chocolate"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/4/45/Chocolate_box_-_Marcolini_01.jpg/960px-Chocolate_box_-_Marcolini_01.jpg",
        ],
        ["plant"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/a/a6/Amaryllis_%28Hippeastrum%29_18-01-2025._%28actm.%29_04.jpg/960px-Amaryllis_%28Hippeastrum%29_18-01-2025._%28actm.%29_04.jpg",
        ],
        ["catfood"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/e/e4/Kattmat-Cat_food-Katzenfutter-Nourriture_pour_chats_-_2022.jpg/960px-Kattmat-Cat_food-Katzenfutter-Nourriture_pour_chats_-_2022.jpg",
            "https://upload.wikimedia.org/wikipedia/commons/thumb/f/fa/Cat_food_%28pellets_and_water%29.jpg/960px-Cat_food_%28pellets_and_water%29.jpg",
        ],
        ["birdcage"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/3/34/Timber_Bird_Cage_vintage.jpg/960px-Timber_Bird_Cage_vintage.jpg",
        ],
        ["aquarium"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/5/52/Goldfish_in_fish_tank.jpg/960px-Goldfish_in_fish_tank.jpg",
        ],
        ["dogleash"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/8/8d/Visitor_walking_happy_dog_leash_hoh_campground_camping_pets_d_archuleta_march_05_2015_%2816751947833%29.jpg/960px-Visitor_walking_happy_dog_leash_hoh_campground_camping_pets_d_archuleta_march_05_2015_%2816751947833%29.jpg",
        ],
        ["drill"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/7/77/Panasonic_Cordless_Drill_%26_Driver_EY1DD2%2C_Ottobrunn_%2820250410-P1046293%29.jpg/960px-Panasonic_Cordless_Drill_%26_Driver_EY1DD2%2C_Ottobrunn_%2820250410-P1046293%29.jpg",
            "https://upload.wikimedia.org/wikipedia/commons/thumb/0/0c/Cordless_electric_%28screw%29_drill.jpg/960px-Cordless_electric_%28screw%29_drill.jpg",
        ],
        ["hammer"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/e/e9/Claw_hammer_2.jpg/960px-Claw_hammer_2.jpg",
            "https://upload.wikimedia.org/wikipedia/commons/thumb/1/16/Claw_hammer-1.jpg/960px-Claw_hammer-1.jpg",
        ],
        ["toolbox"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/4/47/Toolbox_%2849985663631%29.jpg/960px-Toolbox_%2849985663631%29.jpg",
        ],
        ["screwdriver"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/2/24/Precision_Screwdriver_Set_2.jpg/960px-Precision_Screwdriver_Set_2.jpg",
            "https://upload.wikimedia.org/wikipedia/commons/4/4a/Jeweler%27s-screwdriver-set-bright.jpg",
        ],
        // Wall-placeable products («حطها على حايطك»): front-facing shots.
        ["tv"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/6/68/Samsung_UE65F9005STXXE_front_black_20130613.jpg/960px-Samsung_UE65F9005STXXE_front_black_20130613.jpg",
            "https://upload.wikimedia.org/wikipedia/commons/thumb/3/3d/Sanyo_XT-43S7100F_20160808.jpg/960px-Sanyo_XT-43S7100F_20160808.jpg",
            "https://upload.wikimedia.org/wikipedia/commons/thumb/d/d0/Samsung_OLED_TV.jpg/960px-Samsung_OLED_TV.jpg",
        ],
        ["mirror"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/6/6b/Mirror%2C_wall_%28AM_12714-1%29.jpg/960px-Mirror%2C_wall_%28AM_12714-1%29.jpg",
        ],
        ["frame"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/d/dd/Frame_MET_45186.jpg/960px-Frame_MET_45186.jpg",
        ],
        ["art"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/6/64/Abstract_large_painting_%27Dune_landscape%27_painted_by_Dutch_artist_Fons_Heijnsbroek_in_acryl_paint_in_1990%3B_free_download_art_image.jpg/960px-Abstract_large_painting_%27Dune_landscape%27_painted_by_Dutch_artist_Fons_Heijnsbroek_in_acryl_paint_in_1990%3B_free_download_art_image.jpg",
        ],
        ["rug"] = [
            "https://upload.wikimedia.org/wikipedia/commons/thumb/e/ea/Persian_Carpet_%2851759428441%29.jpg/960px-Persian_Carpet_%2851759428441%29.jpg",
            "https://upload.wikimedia.org/wikipedia/commons/0/0f/Indo-Persian_carpet_with_medallions_MET_DP265209.jpg",
        ],
    };
}
