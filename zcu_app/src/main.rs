
mod server;
mod types;
mod rand;
mod ram;
mod commands;
mod config;
mod fpga;
mod chip;
mod vram;
mod gpio;
mod record;
mod task_engine;
mod recorder;
mod tools;

fn main() {

    println!("ZCU Server V3");

    //Attempt to load configuration file
    config::load();
    
    //Initially memory provider
    if crate::config::SIMULATION_MODE {
        //Initalize simulated ram
        vram::init().unwrap();

        println!("WARNING: SIMULATION MODE IS ACTIVE");

        
    } else {
        //Initialize SODIMM card on the PL 
        ram::init().unwrap();

        //Initalize GPIO 
        gpio::init().unwrap();
    }

    //Initialize data recorder
    recorder::init().unwrap();

    //Start ZCU server
    server::run("0.0.0.0:8080").unwrap();    

    
}
