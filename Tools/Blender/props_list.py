"""Accessoires CC0 de Poly Haven intégrés au décor (V0.3.0) : alias Unity, identifiant Poly Haven, budget de triangles,
taille maximale des textures. Partagé par fetch_props.py (téléchargement, système), props_polyhaven.py (Blender) et
convert_props.py (textures, système)."""

PROPS = [
    # alias,          id Poly Haven,                   triangles, texture
    ("WetFloorSign", "WetFloorSign_01", 2500, 1024),
    ("CleanerSpray", "all_purpose_cleaner", 2200, 512),
    ("BleachBottle", "bleach_bottle", 2200, 512),
    ("Cleaner5L", "multi_cleaner_5_litre", 2200, 512),
    ("CleanerBottle", "multi_cleaner_bottle", 2200, 512),
    ("GallonBottle", "plastic_bottle_gallon", 2200, 512),
    ("DrainCleaner", "drain_cleaner", 2200, 512),
    ("SteelShelves", "steel_frame_shelves_01", 6000, 1024),
    ("CardboardBox", "cardboard_box_01", 1200, 1024),
    ("CrateYellow", "plastic_crate_02", 3500, 1024),
    ("CrateRed", "plastic_crate_01", 3500, 1024),
    ("Tote", "industrial_pastic_container", 3500, 1024),
    ("Plant", "potted_plant_02", 9000, 1024),
    ("Broom", "plastic_broom", 2500, 512),
    ("Dustpan", "dustpan", 1200, 512),
    ("Boots", "rubber_boots", 3500, 512),
    ("HoseReel", "garden_hose_wall_mounted_01", 7000, 1024),
    ("PowerBox", "power_box_01", 2500, 1024),
    ("SecurityCamera", "security_camera_01", 2500, 512),
    ("HandTruck", "hand_truck", 4500, 1024),
    ("Chair", "plastic_monobloc_chair_01", 2500, 1024),
    ("ToolChest", "metal_tool_chest", 4500, 1024),
    ("DeskLamp", "desk_lamp_arm_01", 2500, 512),
    ("Clipboard", "clipboard", 600, 512),
    ("Stationery", "stationery_supplies", 2500, 512),
    # Paliers d'aménagement (choisis et vérifiés via le MCP Blender) : canapé capitonné, plante haute, succulente.
    ("Sofa", "sofa_02", 4000, 1024),
    ("PlantTall", "potted_plant_01", 9000, 1024),
    ("Succulent", "potted_plant_04", 1500, 512),
]
