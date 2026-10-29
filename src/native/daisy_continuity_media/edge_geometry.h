#ifndef DAISY_EDGE_GEOMETRY_H
#define DAISY_EDGE_GEOMETRY_H
#include <math.h>
// Side is the destination's location relative to the source: left/right/top/bottom.
struct edge_position { double x, y, width, height; unsigned side; int active; };
static double edge_clamp(double n, double minimum, double maximum) { return fmax(minimum, fmin(maximum, n)); }
static void edge_enter(struct edge_position *p, unsigned side, double fraction) {
    p->side=side; p->active=1; fraction=edge_clamp(fraction,0,1);
    p->x=side==0?p->width-2:side==1?1:fraction*(p->width-1);
    p->y=side==2?p->height-2:side==3?1:fraction*(p->height-1);
}
static int edge_move(struct edge_position *p, double dx, double dy, double *fraction) {
    if(!p->active || !isfinite(dx) || !isfinite(dy)) return 0;
    double x=p->x+dx,y=p->y+dy;
    int back=(p->side==0&&x>=p->width-1)||(p->side==1&&x<=0)||(p->side==2&&y>=p->height-1)||(p->side==3&&y<=0);
    p->x=edge_clamp(x,0,p->width-1);p->y=edge_clamp(y,0,p->height-1);
    if(back){*fraction=p->side<2?p->y/(p->height-1):p->x/(p->width-1);p->active=0;return 1;}
    return 0;
}
#endif
