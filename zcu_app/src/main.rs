
mod server;
mod types;
mod rand;
mod ram;
mod commands;
mod config;
mod chip;
mod vram;

fn main() {
    
    //Initially memory provider
    if(crate::config::SIMULATION_MODE){
        //Initalize simulate ram
        vram::init();
    } else {
        //Initialize SODIMM card on the PL 
        ram::init();
    }

    //Start ZCU server
    server::run("0.0.0.0:8080").unwrap();    

    
}
