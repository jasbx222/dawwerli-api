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
    };
}
