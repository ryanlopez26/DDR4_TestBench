
mod server;
mod types;
mod rand;
mod ram;
mod commands;
mod config;
mod chip;
mod vram;
mod gpio;
mod recorder;

fn main() {
    
    //Initially memory provider
    if crate::config::SIMULATION_MODE {
        //Initalize simulated ram
        vram::init().unwrap();

        
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
