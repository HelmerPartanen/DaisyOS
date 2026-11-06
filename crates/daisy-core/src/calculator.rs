//! Behavioral port of CalculatorViewModel.cs, including its immediate execution
//! order, percent semantics, memory, inverse/hyperbolic functions and G10 display.
use serde::Serialize;
#[derive(Default, Debug, Clone, Copy, Serialize)]
pub enum Angle {
    #[default]
    Deg,
    Rad,
    Gra,
}
#[derive(Debug, Clone, Serialize)]
pub struct Calculator {
    pub display: String,
    pub expression: String,
    pub scientific: bool,
    pub inverse: bool,
    pub hyp: bool,
    pub angle: Angle,
    pub has_memory: bool,
    #[serde(skip)]
    accumulator: f64,
    #[serde(skip)]
    pending: Option<String>,
    #[serde(skip)]
    new_entry: bool,
    #[serde(skip)]
    memory: f64,
}
impl Default for Calculator {
    fn default() -> Self {
        Self {
            display: "0".into(),
            expression: String::new(),
            scientific: false,
            inverse: false,
            hyp: false,
            angle: Angle::Deg,
            has_memory: false,
            accumulator: 0.,
            pending: None,
            new_entry: true,
            memory: 0.,
        }
    }
}
impl Calculator {
    pub fn press(&mut self, key: &str) {
        match key {
            key if key.len() == 1 && (key.as_bytes()[0].is_ascii_digit() || key == ".") => {
                self.digit(key)
            }
            "+" | "-" | "×" | "*" | "÷" | "/" | "^" => self.operator(key),
            "=" => self.equals(),
            "AC" => {
                let memory = self.memory;
                let has_memory = self.has_memory;
                let scientific = self.scientific;
                let angle = self.angle;
                *self = Self::default();
                self.memory = memory;
                self.has_memory = has_memory;
                self.scientific = scientific;
                self.angle = angle;
            }
            "CE" => {
                self.display = "0".into();
                self.new_entry = true;
            }
            "backspace" => self.backspace(),
            "sign" => {
                if let Ok(v) = self.display.parse::<f64>() {
                    self.display = format_result(-v);
                }
            }
            "%" => {
                if let Ok(v) = self.display.parse::<f64>() {
                    self.display = format_result(if self.pending.is_some() {
                        self.accumulator * (v / 100.)
                    } else {
                        v / 100.
                    });
                    self.new_entry = true;
                }
            }
            "mode" => self.scientific = !self.scientific,
            "inv" => self.inverse = !self.inverse,
            "hyp" => self.hyp = !self.hyp,
            "angle" => {
                self.angle = match self.angle {
                    Angle::Deg => Angle::Rad,
                    Angle::Rad => Angle::Gra,
                    Angle::Gra => Angle::Deg,
                }
            }
            "MC" => {
                self.memory = 0.;
                self.has_memory = false;
            }
            "MR" => {
                self.display = format_result(self.memory);
                self.new_entry = true;
            }
            "M+" | "M-" | "MS" => {
                if let Ok(v) = self.display.parse::<f64>() {
                    self.memory = match key {
                        "M+" => self.memory + v,
                        "M-" => self.memory - v,
                        _ => v,
                    };
                    self.has_memory = self.memory != 0.;
                    self.new_entry = true;
                }
            }
            _ => self.function(key),
        }
    }
    fn digit(&mut self, key: &str) {
        if self.new_entry || self.display == "0" || self.display == "Error" {
            self.display = if key == "." { "0.".into() } else { key.into() };
            self.new_entry = false;
        } else if !(key == "." && self.display.contains('.')) && self.display.len() < 16 {
            self.display.push_str(key);
        }
    }
    fn operator(&mut self, key: &str) {
        if let Ok(mut v) = self.display.parse::<f64>() {
            if let Some(op) = &self.pending {
                if !self.new_entry {
                    v = evaluate(self.accumulator, v, op);
                    self.display = format_result(v);
                }
            }
            self.accumulator = v;
            self.pending = Some(key.into());
            self.expression = format!("{} {key}", format_result(v));
            self.new_entry = true;
        }
    }
    fn equals(&mut self) {
        if let (Some(op), Ok(v)) = (&self.pending, self.display.parse::<f64>()) {
            self.expression = format!(
                "{} {} {} =",
                format_result(self.accumulator),
                op,
                format_result(v)
            );
            self.accumulator = evaluate(self.accumulator, v, op);
            self.display = format_result(self.accumulator);
            self.pending = None;
            self.new_entry = true;
        }
    }
    fn backspace(&mut self) {
        if self.new_entry || self.display == "Error" || self.display.len() <= 1 {
            self.display = "0".into();
            self.new_entry = true;
            return;
        }
        self.display.pop();
        if self.display == "-" || self.display.is_empty() {
            self.display = "0".into();
            self.new_entry = true;
        }
    }
    fn to_radians(&self, v: f64) -> f64 {
        match self.angle {
            Angle::Deg => v * (std::f64::consts::PI / 180.),
            Angle::Gra => v * (std::f64::consts::PI / 200.),
            Angle::Rad => v,
        }
    }
    fn radians_to_angle(&self, v: f64) -> f64 {
        match self.angle {
            Angle::Deg => v * (180. / std::f64::consts::PI),
            Angle::Gra => v * (200. / std::f64::consts::PI),
            Angle::Rad => v,
        }
    }
    fn function(&mut self, key: &str) {
        let Ok(v) = self.display.parse::<f64>() else {
            return;
        };
        let a = self.to_radians(v);
        let result = match key {
            "sin" => {
                if self.hyp {
                    if self.inverse {
                        v.asinh()
                    } else {
                        v.sinh()
                    }
                } else if self.inverse {
                    self.radians_to_angle(v.asin())
                } else {
                    a.sin()
                }
            }
            "cos" => {
                if self.hyp {
                    if self.inverse {
                        v.acosh()
                    } else {
                        v.cosh()
                    }
                } else if self.inverse {
                    self.radians_to_angle(v.acos())
                } else {
                    a.cos()
                }
            }
            "tan" => {
                if self.hyp {
                    if self.inverse {
                        v.atanh()
                    } else {
                        v.tanh()
                    }
                } else if self.inverse {
                    self.radians_to_angle(v.atan())
                } else {
                    a.tan()
                }
            }
            "sqrt" => {
                if self.inverse {
                    v * v
                } else {
                    v.sqrt()
                }
            }
            "sqr" => v * v,
            "log" => {
                if self.inverse {
                    10f64.powf(v)
                } else {
                    v.log10()
                }
            }
            "ln" => {
                if self.inverse {
                    v.exp()
                } else {
                    v.ln()
                }
            }
            "recip" => 1. / v,
            "fact" => {
                if !(0. ..=170.).contains(&v) || v.fract() != 0. {
                    f64::NAN
                } else {
                    (2..=v as u32).fold(1., |a, b| a * b as f64)
                }
            }
            "pi" => std::f64::consts::PI,
            _ => return,
        };
        self.display = format_result(result);
        self.new_entry = true;
        self.inverse = false;
        self.hyp = false;
    }
}
fn evaluate(a: f64, b: f64, op: &str) -> f64 {
    match op {
        "+" => a + b,
        "-" => a - b,
        "×" | "*" => a * b,
        "÷" | "/" => {
            if b == 0. {
                f64::NAN
            } else {
                a / b
            }
        }
        "^" => a.powf(b),
        "%" => a * (b / 100.),
        _ => b,
    }
}
/// .NET invariant G10: ten significant digits, fixed for exponents -4..9.
pub fn format_result(v: f64) -> String {
    if !v.is_finite() {
        return "Error".into();
    }
    let negative = v.is_sign_negative();
    let scientific = format!("{:.9e}", v.abs());
    let (mantissa, exponent) = scientific.split_once('e').unwrap();
    let exponent = exponent.parse::<i32>().unwrap();
    let sign = if negative { "-" } else { "" };
    if !(-4..10).contains(&exponent) {
        return format!(
            "{sign}{}E{}{:02}",
            mantissa.trim_end_matches('0').trim_end_matches('.'),
            if exponent >= 0 { "+" } else { "-" },
            exponent.abs()
        );
    }
    let digits = mantissa.replace('.', "");
    let point = exponent + 1;
    let value = if point <= 0 {
        format!("0.{}{}", "0".repeat((-point) as usize), digits)
    } else if point >= digits.len() as i32 {
        format!("{digits}{}", "0".repeat(point as usize - digits.len()))
    } else {
        format!(
            "{}.{}",
            &digits[..point as usize],
            &digits[point as usize..]
        )
    };
    let value = if value.contains('.') {
        value.trim_end_matches('0').trim_end_matches('.')
    } else {
        &value
    };
    format!("{sign}{value}")
}

#[cfg(test)]
mod tests {
    use super::*;
    fn run(keys: &[&str]) -> Calculator {
        let mut c = Calculator::default();
        for key in keys {
            c.press(key);
        }
        c
    }
    #[test]
    fn percent_uses_pending_left_operand() {
        assert_eq!(
            run(&["2", "0", "0", "+", "1", "0", "%", "="]).display,
            "220"
        );
    }
    #[test]
    fn executes_left_to_right_like_existing_app() {
        assert_eq!(run(&["2", "+", "3", "*", "4", "="]).display, "20");
    }
    #[test]
    fn clear_keeps_memory_mode_and_angle() {
        let c = run(&["mode", "angle", "5", "MS", "AC", "MR"]);
        assert_eq!(c.display, "5");
        assert!(c.scientific && c.has_memory);
        assert!(matches!(c.angle, Angle::Rad));
    }
    #[test]
    fn domain_error_recovers_on_digit() {
        assert_eq!(run(&["1", "sign", "sqrt", "3"]).display, "3");
    }
    #[test]
    fn significant_digit_format_boundaries() {
        assert_eq!(format_result(1e10), "1E+10");
        assert_eq!(format_result(1e-5), "1E-05");
        assert_eq!(format_result(0.0001), "0.0001");
        assert_eq!(format_result(-0.0), "-0");
    }
}
