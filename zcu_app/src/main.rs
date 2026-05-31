
mod server;
mod types;
mod rand;
mod ram;
mod commands;
mod config;
mod chip;
mod vram;
mod gpio;

fn main() {
    
    //Initially memory provider
    if(crate::config::SIMULATION_MODE){
        //Initalize simulated ram
        vram::init();

        
    } else {
        //Initialize SODIMM card on the PL 
        ram::init();

        //Initalize GPIO 
        gpio::init();
    }


    //Start ZCU server
    server::run("0.0.0.0:8080").unwrap();    

    
}
