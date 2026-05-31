# ZCU104 user GPIO LEDs (bank 88, VCC1V8_LP)
set_property PACKAGE_PIN D5 [get_ports {led_0[0]}]
set_property PACKAGE_PIN D6 [get_ports {led_0[1]}]
set_property PACKAGE_PIN A5 [get_ports {led_0[2]}]
set_property PACKAGE_PIN B5 [get_ports {led_0[3]}]
set_property IOSTANDARD LVCMOS18 [get_ports {led_0[*]}]

set_false_path -from [get_pins System_i/ddr4_0/inst/u_ddr4_mem_intfc/u_ddr_cal_top/calDone_gated_reg/C] -to [get_pins System_i/debug_leds_0/U0/calib_q1_reg/D]
set_property C_CLK_INPUT_FREQ_HZ 300000000 [get_debug_cores dbg_hub]
set_property C_ENABLE_CLK_DIVIDER false [get_debug_cores dbg_hub]
set_property C_USER_SCAN_CHAIN 1 [get_debug_cores dbg_hub]
connect_debug_port dbg_hub/clk [get_nets clk]
