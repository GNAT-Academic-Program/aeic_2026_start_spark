# Renode setup: STM32F746-DISCO + BMP390 over I2C1

Replicates `I2C_Bmp` (BMP390 @ I2C1, address 0x77, SCL=PB8 / SDA=PB9) so it
runs unmodified in Renode.

## Files
- `bmp390.cs` — BMP390 I2C model (CHIP_ID 0x60, calibration block, PWR_CTRL
  read-back, STATUS data-ready, compensable raw temperature). Renode has no
  built-in BMP390, so this is required.
- `stm32f746-bmp390.repl` — extends the stock `platforms/cpus/stm32f746.repl`
  and registers the sensor on `i2c1` at 0x77.
- `i2c_bmp.resc` — compiles the model, loads platform + ELF, opens the UART.

## Run
Put all four files in one directory, then:

    renode
    (monitor) i @i2c_bmp.resc
    (monitor) start

The default path in `i2c_bmp.resc` is already set to `/home/henley/Desktop/baremetal_ada/ghal_examples/bin/i2c_bmp`. Edit the `$bin?=` line in `i2c_bmp.resc` if your binary is elsewhere.

The Alire/GNAT bare-metal build emits an ELF (no extension); point `$bin` at it.

## Expected output
`Debug.Put_Line` streams to the USART1 analyzer window:

    Opening BMP390...
    BMP390 open OK
    pwr_ctrl: 0x32        # Press_En=0, Temp_En=1, Normal mode (bits 5:4 = 11)
    temp:  2.49...E+01 C  # ~25 C
    ...

## Tuning the sensor (Monitor)
    sysbus.i2c1.bmp390 Temperature              # what the firmware should compute
    sysbus.i2c1.bmp390 RawTemperature 8200000   # warmer; re-reads pick it up live

## Notes / caveats
- The compensated value assumes your `BMP` package uses Bosch's standard float
  temperature formula (par_t1 = NVM_T1*256, par_t2 = NVM_T2/2^30,
  par_t3 = NVM_T3/2^48). If you use the integer/fixed path or a different scaling,
  the printed number shifts — tune `RawTemperature` or the 0x31..0x35 bytes.
- Pressure is `Press_En => 0` in the firmware; the pressure calibration bytes
  are placeholders and not tuned.
- Renode does not model GPIO alternate-function muxing for I2C — `i2c1` forwards
  to its registered child regardless of the PB8/PB9 AF4 setup in `Board.Initialize`.
- If your `Debug` channel is not USART1, change `showAnalyzer sysbus.usart1`.
