import plistlib
from pathlib import Path
import runpy
import tempfile
import unittest


launcher = runpy.run_path(str(Path(__file__).resolve().parents[1] / "Start-MacRaid.py"))
server_executable = launcher["server_executable"]
RaidError = launcher["RaidError"]


class MacServerLayoutTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.build = Path(temporary.name) / "HeavyPocketsServer.app"
        self.build.mkdir()

    def test_dedicated_player_beside_libraries(self):
        for name in ("Heavy Pockets", "UnityPlayer.dylib", "libmono-native.dylib"):
            path = self.build / name
            path.touch(mode=0o755)
        player = self.build / "Heavy Pockets"
        self.assertEqual(server_executable(self.build), player)
        player.chmod(0o644)
        with self.assertRaisesRegex(RaidError, "not executable"):
            server_executable(self.build)
        player.unlink()
        with self.assertRaisesRegex(RaidError, "build exists"):
            server_executable(self.build)

    def test_standard_bundle_uses_its_declared_executable(self):
        contents = self.build / "Contents"
        (contents / "MacOS").mkdir(parents=True)
        player = contents / "MacOS" / "ServerPlayer"
        player.touch(mode=0o755)
        (contents / "Info.plist").write_bytes(plistlib.dumps({"CFBundleExecutable": player.name}))
        self.assertEqual(server_executable(self.build), player)


if __name__ == "__main__":
    unittest.main()
