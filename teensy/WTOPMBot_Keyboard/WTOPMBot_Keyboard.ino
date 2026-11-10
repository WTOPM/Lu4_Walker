/*
 * WTOPMBot — Teensy 4.0 USB HID keyboard only (no mouse for now).
 *
 * Tools → USB Type: Serial + Keyboard + Mouse + Joystick  (usb=serialhid still OK)
 *   or Serial + Keyboard
 *
 * Protocol 115200:
 *   TAP <vkHex> [holdMs]     e.g. TAP 31 72
 *   PING
 *
 * Maps all bot panels: F1–F12, 0–9 - =, Num0–9 Num/ Num*, letters A–Z
 */

#include <Keyboard.h>

void setup() {
  Serial.begin(115200);
  delay(500);
  Keyboard.begin();
  randomSeed(analogRead(A0) ^ millis());
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
    handleTap(line.substring(4));
  } else if (line == "PING") {
    Serial.println("PONG");
  } else {
    // Ignore obsolete CLICK/MOVE from older bot builds.
    Serial.println("ERR cmd");
  }
}

void handleTap(String args) {
  args.trim();
  int sp = args.indexOf(' ');
  String hex = (sp < 0) ? args : args.substring(0, sp);
  int holdMs = (sp < 0) ? 0 : args.substring(sp + 1).toInt();

  long vk = strtol(hex.c_str(), nullptr, 16);
  if (vk <= 0 || vk > 0xFF) {
    Serial.println("ERR vk");
    return;
  }

  int key = mapVk((int)vk);
  if (key == 0) {
    Serial.println("ERR map");
    return;
  }

  if (holdMs < 25) holdMs = humanHoldMs();
  if (holdMs > 400) holdMs = 400;

  delay(random(8, 35));
  Keyboard.press(key);
  delay(holdMs);
  Keyboard.release(key);
  delay(random(12, 45));
  Serial.println("OK");
}

int humanHoldMs() {
  int roll = random(100);
  if (roll < 10) return random(30, 55);
  if (roll < 22) return random(140, 220);
  return random(50, 130);
}

int mapVk(int vk) {
  if ((vk >= 0x30 && vk <= 0x39) || (vk >= 0x41 && vk <= 0x5A))
    return vk;

  if (vk >= 0x70 && vk <= 0x7B)
    return KEY_F1 + (vk - 0x70);

  if (vk >= 0x60 && vk <= 0x69)
    return KEYPAD_0 + (vk - 0x60);

  switch (vk) {
    case 0x20: return ' ';
    case 0x0D: return KEY_ENTER;
    case 0x1B: return KEY_ESC;
    case 0x09: return KEY_TAB;
    case 0xBD: return '-';
    case 0xBB: return '=';
    case 0x6A: return KEYPAD_ASTERIX;
    case 0x6F: return KEYPAD_SLASH;
    case 0x6B: return KEYPAD_PLUS;
    case 0x6D: return KEYPAD_MINUS;
    case 0x6E: return KEYPAD_PERIOD;
    default: return 0;
  }
}
