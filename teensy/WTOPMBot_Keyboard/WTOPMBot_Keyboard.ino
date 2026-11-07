/*
 * WTOPMBot — Teensy 4.x as a real USB HID keyboard.
 * Serial 115200, line: TAP <vkHex>   e.g. TAP 31 -> digit 1
 *
 * Arduino IDE: Board Teensy 4.x, USB Type = Keyboard + Serial
 */

void setup() {
  Serial.begin(115200);
  delay(300);
  Serial.println("WTOPMBot Teensy ready");
}

void loop() {
  if (!Serial.available())
    return;

  String line = Serial.readStringUntil('\n');
  line.trim();
  if (line.length() == 0)
    return;

  if (line.startsWith("TAP ")) {
    String hex = line.substring(4);
    hex.trim();
    long vk = strtol(hex.c_str(), nullptr, 16);
    if (vk > 0 && vk <= 0xFF) {
      tapVk((int)vk);
      Serial.println("OK");
    } else {
      Serial.println("ERR vk");
    }
  } else if (line == "PING") {
    Serial.println("PONG");
  }
}

void tapVk(int vk) {
  int key = mapVk(vk);
  if (key == 0)
    return;

  Keyboard.press(key);
  delay(random(35, 75));
  Keyboard.release(key);
}

int mapVk(int vk) {
  // Windows VK for 0-9 / A-Z match ASCII — Teensy accepts them directly.
  if ((vk >= 0x30 && vk <= 0x39) || (vk >= 0x41 && vk <= 0x5A))
    return vk;

  if (vk >= 0x70 && vk <= 0x7B)
    return KEY_F1 + (vk - 0x70);

  switch (vk) {
    case 0x20: return ' ';
    case 0x0D: return KEY_ENTER;
    case 0x1B: return KEY_ESC;
    case 0x09: return KEY_TAB;
    default: return 0;
  }
}
