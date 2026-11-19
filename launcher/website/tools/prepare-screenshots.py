"""Create fictional launcher data for public screenshots; never reads user game files."""
import datetime
import gzip
import hashlib
import json
from pathlib import Path
import struct
import uuid

launcher = Path(__file__).resolve().parents[2]
fixture = launcher / "artifacts" / "island-website" / "fixture"
game = fixture / "demo-game"
packs = fixture / "demo-packs"
game.mkdir(parents=True, exist_ok=True)
packs.mkdir(parents=True, exist_ok=True)
played = datetime.datetime(2026, 10, 24, 18, 40, tzinfo=datetime.timezone(datetime.timedelta(hours=8)))
instances = [
    ("ForestCottage", "林间小屋", "1.21.1", "Vanilla"),
    ("IslandJourney", "群岛漫游", "1.21.1", "Fabric"),
    ("StarWorkshop", "星河工坊", "1.20.1", "Forge"),
    ("MorningLight", "晨光与远山", "1.21.1", "NeoForge"),
    ("QuietMeadow", "静谧原野", "1.20.1", "Vanilla"),
    ("NewAdventure", "新的冒险", "1.21.1", "Fabric"),
]
preferences = {}
for i, (identifier, label, minecraft, loader) in enumerate(instances):
    directory = game / "versions" / identifier
    directory.mkdir(parents=True, exist_ok=True)
    (directory / f"{identifier}.json").write_text(json.dumps({"id": identifier, "mainClass": "net.minecraft.client.main.Main", "javaVersion": {"majorVersion": 21}}), encoding="utf-8")
    (directory / "blockdock-instance.json").write_text(json.dumps({"minecraft": minecraft, "loader": loader}), encoding="utf-8")
    key = str(game).rstrip("/\\").upper() + "|" + identifier
    preferences[key] = {"DisplayName": label, "Favorite": i < 2, "LastPlayed": (played - datetime.timedelta(days=i)).isoformat(), "PlayedMinutes": 0}

def text(value):
    encoded = value.encode("utf-8")
    return struct.pack(">H", len(encoded)) + encoded

world = game / "versions" / "ForestCottage" / "saves" / "ForestCottage"
world.mkdir(parents=True, exist_ok=True)
nbt = b"\x0a" + text("") + b"\x0a" + text("Data") + b"\x08" + text("LevelName") + text("林间小屋") + b"\x04" + text("LastPlayed") + struct.pack(">q", int(played.timestamp() * 1000)) + b"\x00\x00"
(world / "level.dat").write_bytes(gzip.compress(nbt))

account_id = "island-website-demo"
offline_id = uuid.UUID(bytes=hashlib.md5(b"OfflinePlayer:IslandPlayer").digest(), version=3).hex
state = {
    "GameDirectory": str(game), "SelectedGameFolderId": "demo-worlds", "SelectedInstanceId": "ForestCottage",
    "GameFolders": [
        {"Id": "demo-worlds", "Name": "我的游戏", "Directory": str(game), "Isolation": 1},
        {"Id": "demo-packs", "Name": "整合包收藏", "Directory": str(packs), "Isolation": 1},
    ],
    "Accounts": [{"Id": account_id, "Name": "IslandPlayer", "Uuid": offline_id, "Microsoft": False}],
    "SelectedAccountId": account_id, "Instances": preferences, "MemoryMb": 4096, "AutoMemory": True,
    "Appearance": {"CardAnimation": 1},
}
for theme in ("forest", "rose", "glass"):
    settings = fixture / f"settings-{theme}"
    settings.mkdir(parents=True, exist_ok=True)
    (settings / "settings.json").write_text(json.dumps(state, ensure_ascii=False, indent=2), encoding="utf-8")
print("Fictional screenshot fixture prepared:", fixture)
