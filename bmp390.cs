//
// BMP390.cs — minimal Bosch BMP390 I2C model for Renode.
//
// Implements just enough of the BMP390 register map to satisfy a typical
// Ada/SPARK driver: CHIP_ID check, calibration block, PWR_CTRL read-back,
// STATUS data-ready flags, and a compensable raw temperature.
//
// Register-pointer semantics mirror Renode's built-in I2C sensors
// (cf. Peripherals/I2C/TCA6416.cs): first written byte selects the register,
// subsequent bytes write, reads auto-increment the pointer by +1.
//
// Include from a .resc with:  include @bmp390.cs
// Instantiate from a .repl with:  bmp390: I2C.BMP390 @ i2c1 0x77
//

using System;
using Antmicro.Renode.Logging;

namespace Antmicro.Renode.Peripherals.I2C
{
    public class BMP390 : II2CPeripheral
    {
        public BMP390()
        {
            registers = new byte[256];
            Reset();
        }

        public void Reset()
        {
            Array.Clear(registers, 0, registers.Length);
            pointer = 0;
            collectingAddress = true;
            InitDefaults();
        }

        // --- II2CPeripheral ----------------------------------------------------

        public void Write(byte[] data)
        {
            foreach(var b in data)
            {
                if(collectingAddress)
                {
                    pointer = b;
                    collectingAddress = false;
                    this.NoisyLog("Register pointer set to 0x{0:X2}", pointer);
                }
                else
                {
                    WriteRegister(pointer, b);
                    pointer = (byte)(pointer + 1);
                }
            }
        }

        public byte[] Read(int count)
        {
            var result = new byte[count];
            for(var i = 0; i < count; i++)
            {
                result[i] = registers[pointer];
                this.NoisyLog("Read 0x{0:X2} from register 0x{1:X2}", result[i], pointer);
                pointer = (byte)(pointer + 1);
            }
            return result;
        }

        public void FinishTransmission()
        {
            // A STOP/START ends the address phase but the pointer is preserved,
            // so a repeated-start "set address, then read" sequence works.
            collectingAddress = true;
        }

        // --- monitor-facing knobs ---------------------------------------------

        // 24-bit raw temperature word (registers 0x07..0x09, little-endian).
        // Default lands the compensated reading at ~25 C with the calibration
        // values set in InitDefaults(). Poke it from the Monitor to sweep, e.g.:
        //   sysbus.i2c1.bmp390 RawTemperature 8200000
        public uint RawTemperature
        {
            get => (uint)(registers[0x07] | (registers[0x08] << 8) | (registers[0x09] << 16));
            set
            {
                registers[0x07] = (byte)(value & 0xFF);
                registers[0x08] = (byte)((value >> 8) & 0xFF);
                registers[0x09] = (byte)((value >> 16) & 0xFF);
            }
        }

        // Compensated temperature in C, using the standard Bosch float formula.
        // Read-only; lets you confirm in the Monitor what the firmware should print.
        public double Temperature
        {
            get
            {
                var t1 = (ushort)(registers[0x31] | (registers[0x32] << 8));
                var t2 = (ushort)(registers[0x33] | (registers[0x34] << 8));
                var t3 = (sbyte)registers[0x35];

                var parT1 = t1 / Math.Pow(2, -8);
                var parT2 = t2 / Math.Pow(2, 30);
                var parT3 = t3 / Math.Pow(2, 48);

                double partial1 = RawTemperature - parT1;
                double partial2 = partial1 * parT2;
                return partial2 + (partial1 * partial1) * parT3;
            }
        }

        // --- internals ---------------------------------------------------------

        private void WriteRegister(byte address, byte value)
        {
            switch(address)
            {
            case CmdRegister:
                if(value == SoftResetCommand)
                {
                    this.Log(LogLevel.Info, "Soft reset (CMD=0xB6)");
                    var savedRaw = RawTemperature; // keep the user-set temperature across reset
                    InitDefaults();
                    RawTemperature = savedRaw;
                }
                else
                {
                    registers[address] = value;
                }
                break;
            default:
                // PWR_CTRL (0x1B), OSR (0x1C), ODR (0x1D), CONFIG (0x1F), etc.
                // are stored verbatim so the firmware can read them back.
                registers[address] = value;
                this.NoisyLog("Wrote 0x{0:X2} to register 0x{1:X2}", value, address);
                break;
            }
        }

        private void InitDefaults()
        {
            // Identity / status
            registers[0x00] = 0x60; // CHIP_ID = BMP390
            registers[0x01] = 0x01; // REV_ID
            registers[0x02] = 0x00; // ERR_REG: no errors
            registers[0x03] = 0x70; // STATUS: cmd_rdy | drdy_press | drdy_temp

            // Temperature calibration (datasheet 0x31..0x35), little-endian.
            //   NVM_PAR_T1 = 27504  -> par_t1 = 27504 * 256
            //   NVM_PAR_T2 = 26435  -> par_t2 = 26435 / 2^30
            //   NVM_PAR_T3 = -8     -> par_t3 = -8 / 2^48
            registers[0x31] = 0x70; registers[0x32] = 0x6B; // T1 = 0x6B70
            registers[0x33] = 0x43; registers[0x34] = 0x67; // T2 = 0x6743
            registers[0x35] = 0xF8;                         // T3 = -8

            // Pressure calibration (0x36..0x45): plausible non-zero placeholders.
            // The reference firmware has Press_En => 0, so these are unused; set
            // so a future pressure read is not absurd, but they are NOT tuned.
            registers[0x36] = 0xF4; registers[0x37] = 0x7B; // P1
            registers[0x38] = 0xF6; registers[0x39] = 0xB6; // P2
            registers[0x3A] = 0xF5;                         // P3
            registers[0x3B] = 0x00;                         // P4
            registers[0x3C] = 0x3C; registers[0x3D] = 0x6E; // P5
            registers[0x3E] = 0x3C; registers[0x3F] = 0x66; // P6
            registers[0x40] = 0xF6;                         // P7
            registers[0x41] = 0xF2;                         // P8
            registers[0x42] = 0x5E; registers[0x43] = 0x16; // P9
            registers[0x44] = 0x18;                         // P10
            registers[0x45] = 0xF0;                         // P11

            // Raw temperature ~ 25 C with the calibration above.
            RawTemperature = 8056000;
        }

        private byte[] registers;
        private byte pointer;
        private bool collectingAddress;

        private const byte CmdRegister = 0x7E;
        private const byte SoftResetCommand = 0xB6;
    }
}
