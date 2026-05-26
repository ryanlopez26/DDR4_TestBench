
mod server;
mod types;
mod rand;
mod ram;
mod commands;
mod config;
mod chip;
mod vram;

fn main() {
    
    //Start ZCU server
    server::run("0.0.0.0:8080").unwrap();    

    
}
