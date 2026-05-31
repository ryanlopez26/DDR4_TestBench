#######################################################################
# ZCU104 (XCZU7EV-2FFVC1156) — CUSTOM DDR4 constraints
#
# MIG DDR4 board interface = Custom (this XDC owns all pins; MIG will
# not overwrite). Port names match the System.bd top-level entity:
#   clk_300mhz_clk_p/n, ddr4_sdram_0_*, led_0
#
# Card: single-rank x16, 64-bit (MTA4ATF51264HZ-2G6).
#   -> single bg/cke/cs_n/odt/ck; 17-bit adr (adr14..16 carry the
#      RAS/CAS/WE functions within the bus, no separate ports).
#
# PACKAGE_PIN values transcribed verbatim from:
#   /home/rylo/xilinx-boards/boards/Xilinx/zcu104/1.1/part0_pins.xml
#
# NOTE: board file lists bg1/cke1/cs1_n/ck1/odt1 (dual-rank wiring),
# but this single-rank design exposes only rank-0 ports, so rank-1
# pins (AB16/AM15/AL17/AJ16/AJ15/AM16) are intentionally unused.
#######################################################################


#######################################################################
# 1. 300 MHz system reference clock  (CLK_300, DIFF_SSTL12)
#######################################################################
set_property PACKAGE_PIN AH18 [get_ports {clk_300mhz_clk_p}]
set_property PACKAGE_PIN AH17 [get_ports {clk_300mhz_clk_n}]
set_property IOSTANDARD DIFF_SSTL12 [get_ports {clk_300mhz_clk_p}]
set_property IOSTANDARD DIFF_SSTL12 [get_ports {clk_300mhz_clk_n}]


#######################################################################
# 2. DDR4 address / command
#######################################################################
set_property PACKAGE_PIN AC17 [get_ports {ddr4_sdram_0_act_n}]
set_property PACKAGE_PIN AH16 [get_ports {ddr4_sdram_0_adr[0]}]
set_property PACKAGE_PIN AG14 [get_ports {ddr4_sdram_0_adr[1]}]
set_property PACKAGE_PIN AG15 [get_ports {ddr4_sdram_0_adr[2]}]
set_property PACKAGE_PIN AF15 [get_ports {ddr4_sdram_0_adr[3]}]
set_property PACKAGE_PIN AF16 [get_ports {ddr4_sdram_0_adr[4]}]
set_property PACKAGE_PIN AJ14 [get_ports {ddr4_sdram_0_adr[5]}]
set_property PACKAGE_PIN AH14 [get_ports {ddr4_sdram_0_adr[6]}]
set_property PACKAGE_PIN AF17 [get_ports {ddr4_sdram_0_adr[7]}]
set_property PACKAGE_PIN AK17 [get_ports {ddr4_sdram_0_adr[8]}]
set_property PACKAGE_PIN AJ17 [get_ports {ddr4_sdram_0_adr[9]}]
set_property PACKAGE_PIN AK14 [get_ports {ddr4_sdram_0_adr[10]}]
set_property PACKAGE_PIN AK15 [get_ports {ddr4_sdram_0_adr[11]}]
set_property PACKAGE_PIN AL18 [get_ports {ddr4_sdram_0_adr[12]}]
set_property PACKAGE_PIN AK18 [get_ports {ddr4_sdram_0_adr[13]}]
set_property PACKAGE_PIN AA16 [get_ports {ddr4_sdram_0_adr[14]}]
set_property PACKAGE_PIN AA14 [get_ports {ddr4_sdram_0_adr[15]}]
set_property PACKAGE_PIN AD15 [get_ports {ddr4_sdram_0_adr[16]}]
set_property PACKAGE_PIN AL15 [get_ports {ddr4_sdram_0_ba[0]}]
set_property PACKAGE_PIN AL16 [get_ports {ddr4_sdram_0_ba[1]}]
set_property PACKAGE_PIN AC16 [get_ports {ddr4_sdram_0_bg[0]}]
set_property PACKAGE_PIN AD17 [get_ports {ddr4_sdram_0_cke[0]}]
set_property PACKAGE_PIN AA15 [get_ports {ddr4_sdram_0_cs_n[0]}]
set_property PACKAGE_PIN AE15 [get_ports {ddr4_sdram_0_odt[0]}]


#######################################################################
# 3. DDR4 clock  (DIFF_SSTL12_DCI)
#######################################################################
set_property PACKAGE_PIN AF18 [get_ports {ddr4_sdram_0_ck_t[0]}]
set_property PACKAGE_PIN AG18 [get_ports {ddr4_sdram_0_ck_c[0]}]


#######################################################################
# 4. DDR4 data mask / DBI  (POD12_DCI)
#   External BD port is dm_n (internal IP pin is dm_dbi_n).
#######################################################################
set_property PACKAGE_PIN AH22 [get_ports {ddr4_sdram_0_dm_n[0]}]
set_property PACKAGE_PIN AE18 [get_ports {ddr4_sdram_0_dm_n[1]}]
set_property PACKAGE_PIN AL20 [get_ports {ddr4_sdram_0_dm_n[2]}]
set_property PACKAGE_PIN AP19 [get_ports {ddr4_sdram_0_dm_n[3]}]
set_property PACKAGE_PIN AF11 [get_ports {ddr4_sdram_0_dm_n[4]}]
set_property PACKAGE_PIN AH12 [get_ports {ddr4_sdram_0_dm_n[5]}]
set_property PACKAGE_PIN AK13 [get_ports {ddr4_sdram_0_dm_n[6]}]
set_property PACKAGE_PIN AN12 [get_ports {ddr4_sdram_0_dm_n[7]}]


#######################################################################
# 5. DDR4 data  DQ[63:0]  (POD12_DCI)
#######################################################################
set_property PACKAGE_PIN AE24 [get_ports {ddr4_sdram_0_dq[0]}]
set_property PACKAGE_PIN AE23 [get_ports {ddr4_sdram_0_dq[1]}]
set_property PACKAGE_PIN AF22 [get_ports {ddr4_sdram_0_dq[2]}]
set_property PACKAGE_PIN AF21 [get_ports {ddr4_sdram_0_dq[3]}]
set_property PACKAGE_PIN AG20 [get_ports {ddr4_sdram_0_dq[4]}]
set_property PACKAGE_PIN AG19 [get_ports {ddr4_sdram_0_dq[5]}]
set_property PACKAGE_PIN AH21 [get_ports {ddr4_sdram_0_dq[6]}]
set_property PACKAGE_PIN AG21 [get_ports {ddr4_sdram_0_dq[7]}]
set_property PACKAGE_PIN AA20 [get_ports {ddr4_sdram_0_dq[8]}]
set_property PACKAGE_PIN AA19 [get_ports {ddr4_sdram_0_dq[9]}]
set_property PACKAGE_PIN AD19 [get_ports {ddr4_sdram_0_dq[10]}]
set_property PACKAGE_PIN AC18 [get_ports {ddr4_sdram_0_dq[11]}]
set_property PACKAGE_PIN AE20 [get_ports {ddr4_sdram_0_dq[12]}]
set_property PACKAGE_PIN AD20 [get_ports {ddr4_sdram_0_dq[13]}]
set_property PACKAGE_PIN AC19 [get_ports {ddr4_sdram_0_dq[14]}]
set_property PACKAGE_PIN AB19 [get_ports {ddr4_sdram_0_dq[15]}]
set_property PACKAGE_PIN AJ22 [get_ports {ddr4_sdram_0_dq[16]}]
set_property PACKAGE_PIN AJ21 [get_ports {ddr4_sdram_0_dq[17]}]
set_property PACKAGE_PIN AK20 [get_ports {ddr4_sdram_0_dq[18]}]
set_property PACKAGE_PIN AJ20 [get_ports {ddr4_sdram_0_dq[19]}]
set_property PACKAGE_PIN AK19 [get_ports {ddr4_sdram_0_dq[20]}]
set_property PACKAGE_PIN AJ19 [get_ports {ddr4_sdram_0_dq[21]}]
set_property PACKAGE_PIN AL23 [get_ports {ddr4_sdram_0_dq[22]}]
set_property PACKAGE_PIN AL22 [get_ports {ddr4_sdram_0_dq[23]}]
set_property PACKAGE_PIN AN23 [get_ports {ddr4_sdram_0_dq[24]}]
set_property PACKAGE_PIN AM23 [get_ports {ddr4_sdram_0_dq[25]}]
set_property PACKAGE_PIN AP23 [get_ports {ddr4_sdram_0_dq[26]}]
set_property PACKAGE_PIN AN22 [get_ports {ddr4_sdram_0_dq[27]}]
set_property PACKAGE_PIN AP22 [get_ports {ddr4_sdram_0_dq[28]}]
set_property PACKAGE_PIN AP21 [get_ports {ddr4_sdram_0_dq[29]}]
set_property PACKAGE_PIN AN19 [get_ports {ddr4_sdram_0_dq[30]}]
set_property PACKAGE_PIN AM19 [get_ports {ddr4_sdram_0_dq[31]}]
set_property PACKAGE_PIN AC13 [get_ports {ddr4_sdram_0_dq[32]}]
set_property PACKAGE_PIN AB13 [get_ports {ddr4_sdram_0_dq[33]}]
set_property PACKAGE_PIN AF12 [get_ports {ddr4_sdram_0_dq[34]}]
set_property PACKAGE_PIN AE12 [get_ports {ddr4_sdram_0_dq[35]}]
set_property PACKAGE_PIN AF13 [get_ports {ddr4_sdram_0_dq[36]}]
set_property PACKAGE_PIN AE13 [get_ports {ddr4_sdram_0_dq[37]}]
set_property PACKAGE_PIN AE14 [get_ports {ddr4_sdram_0_dq[38]}]
set_property PACKAGE_PIN AD14 [get_ports {ddr4_sdram_0_dq[39]}]
set_property PACKAGE_PIN AG8  [get_ports {ddr4_sdram_0_dq[40]}]
set_property PACKAGE_PIN AF8  [get_ports {ddr4_sdram_0_dq[41]}]
set_property PACKAGE_PIN AG10 [get_ports {ddr4_sdram_0_dq[42]}]
set_property PACKAGE_PIN AG11 [get_ports {ddr4_sdram_0_dq[43]}]
set_property PACKAGE_PIN AH13 [get_ports {ddr4_sdram_0_dq[44]}]
set_property PACKAGE_PIN AG13 [get_ports {ddr4_sdram_0_dq[45]}]
set_property PACKAGE_PIN AJ11 [get_ports {ddr4_sdram_0_dq[46]}]
set_property PACKAGE_PIN AH11 [get_ports {ddr4_sdram_0_dq[47]}]
set_property PACKAGE_PIN AK9  [get_ports {ddr4_sdram_0_dq[48]}]
set_property PACKAGE_PIN AJ9  [get_ports {ddr4_sdram_0_dq[49]}]
set_property PACKAGE_PIN AK10 [get_ports {ddr4_sdram_0_dq[50]}]
set_property PACKAGE_PIN AJ10 [get_ports {ddr4_sdram_0_dq[51]}]
set_property PACKAGE_PIN AL12 [get_ports {ddr4_sdram_0_dq[52]}]
set_property PACKAGE_PIN AK12 [get_ports {ddr4_sdram_0_dq[53]}]
set_property PACKAGE_PIN AL10 [get_ports {ddr4_sdram_0_dq[54]}]
set_property PACKAGE_PIN AL11 [get_ports {ddr4_sdram_0_dq[55]}]
set_property PACKAGE_PIN AM8  [get_ports {ddr4_sdram_0_dq[56]}]
set_property PACKAGE_PIN AM9  [get_ports {ddr4_sdram_0_dq[57]}]
set_property PACKAGE_PIN AM10 [get_ports {ddr4_sdram_0_dq[58]}]
set_property PACKAGE_PIN AM11 [get_ports {ddr4_sdram_0_dq[59]}]
set_property PACKAGE_PIN AP11 [get_ports {ddr4_sdram_0_dq[60]}]
set_property PACKAGE_PIN AN11 [get_ports {ddr4_sdram_0_dq[61]}]
set_property PACKAGE_PIN AP9  [get_ports {ddr4_sdram_0_dq[62]}]
set_property PACKAGE_PIN AP10 [get_ports {ddr4_sdram_0_dq[63]}]


#######################################################################
# 6. DDR4 data strobes  DQS_T/C[7:0]  (DIFF_POD12_DCI)
#######################################################################
set_property PACKAGE_PIN AF23 [get_ports {ddr4_sdram_0_dqs_t[0]}]
set_property PACKAGE_PIN AG23 [get_ports {ddr4_sdram_0_dqs_c[0]}]
set_property PACKAGE_PIN AA18 [get_ports {ddr4_sdram_0_dqs_t[1]}]
set_property PACKAGE_PIN AB18 [get_ports {ddr4_sdram_0_dqs_c[1]}]
set_property PACKAGE_PIN AK22 [get_ports {ddr4_sdram_0_dqs_t[2]}]
set_property PACKAGE_PIN AK23 [get_ports {ddr4_sdram_0_dqs_c[2]}]
set_property PACKAGE_PIN AM21 [get_ports {ddr4_sdram_0_dqs_t[3]}]
set_property PACKAGE_PIN AN21 [get_ports {ddr4_sdram_0_dqs_c[3]}]
set_property PACKAGE_PIN AC12 [get_ports {ddr4_sdram_0_dqs_t[4]}]
set_property PACKAGE_PIN AD12 [get_ports {ddr4_sdram_0_dqs_c[4]}]
set_property PACKAGE_PIN AG9  [get_ports {ddr4_sdram_0_dqs_t[5]}]
set_property PACKAGE_PIN AH9  [get_ports {ddr4_sdram_0_dqs_c[5]}]
set_property PACKAGE_PIN AK8  [get_ports {ddr4_sdram_0_dqs_t[6]}]
set_property PACKAGE_PIN AL8  [get_ports {ddr4_sdram_0_dqs_c[6]}]
set_property PACKAGE_PIN AN9  [get_ports {ddr4_sdram_0_dqs_t[7]}]
set_property PACKAGE_PIN AN8  [get_ports {ddr4_sdram_0_dqs_c[7]}]


#######################################################################
# 7. DDR4 reset  (LVCMOS12, drive 8)
#######################################################################
set_property PACKAGE_PIN AB14 [get_ports {ddr4_sdram_0_reset_n}]


#######################################################################
# 8. IOSTANDARDs (bulk)
#######################################################################
set_property IOSTANDARD SSTL12_DCI [get_ports {ddr4_sdram_0_adr[*] ddr4_sdram_0_ba[*] ddr4_sdram_0_bg[*] ddr4_sdram_0_cke[*] ddr4_sdram_0_cs_n[*] ddr4_sdram_0_odt[*] ddr4_sdram_0_act_n}]
set_property IOSTANDARD DIFF_SSTL12_DCI [get_ports {ddr4_sdram_0_ck_t[*] ddr4_sdram_0_ck_c[*]}]
set_property IOSTANDARD POD12_DCI [get_ports {ddr4_sdram_0_dq[*] ddr4_sdram_0_dm_n[*]}]
set_property IOSTANDARD DIFF_POD12_DCI [get_ports {ddr4_sdram_0_dqs_t[*] ddr4_sdram_0_dqs_c[*]}]
set_property IOSTANDARD LVCMOS12 [get_ports {ddr4_sdram_0_reset_n}]
set_property DRIVE 8 [get_ports {ddr4_sdram_0_reset_n}]


#######################################################################
# 9. User GPIO LEDs  (PL bank 88, LVCMOS33, active-high)
#   Verified against part0_pins.xml (GPIO_LED_0..3_LS).
#######################################################################
set_property PACKAGE_PIN D5 [get_ports {led_0[0]}]   ;# GPIO_LED_0 / DS38
set_property PACKAGE_PIN D6 [get_ports {led_0[1]}]   ;# GPIO_LED_1 / DS37
set_property PACKAGE_PIN A5 [get_ports {led_0[2]}]   ;# GPIO_LED_2 / DS39
set_property PACKAGE_PIN B5 [get_ports {led_0[3]}]   ;# GPIO_LED_3 / DS40
set_property IOSTANDARD LVCMOS33 [get_ports {led_0[*]}]
